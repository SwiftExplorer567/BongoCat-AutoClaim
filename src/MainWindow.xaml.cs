using System.Runtime.InteropServices;
using System.Windows.Input;
using BongoCatAutoClaim.Core;
using H.NotifyIcon;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Storage.Pickers;

namespace BongoCatAutoClaim;

public sealed partial class MainWindow : Window
{
    private const int WindowWidth = 580;
    private const int WindowHeight = 900;

    private readonly Settings _settings = Settings.Load();
    private readonly PatchService _service = new();
    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TaskbarIcon _tray;
    private bool _allowClose;
    private bool _trayHintShown;
    private ToggleMenuFlyoutItem? _autoClaimItem;
    private bool _suppressEvents;
    private string _lastActivity = "";

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ConfigureAppWindow();

        _tray = CreateTrayIcon();

        string? dir = _settings.GameDirectory;
        if (!SteamLocator.IsValidBongoCatDirectory(dir))
        {
            dir = SteamLocator.FindBongoCatDirectory();
        }

        _service.AutoPatchEnabled = _settings.AutoPatch;
        _service.SetGameDirectory(dir);
        _service.StateChanged += () => DispatcherQueue.TryEnqueue(UpdateStatus);
        _service.NoticeRaised += n => DispatcherQueue.TryEnqueue(() => OnNotice(n));

        _suppressEvents = true;
        AutoPatchToggle.IsOn = _settings.AutoPatch;
        StartupToggle.IsOn = Settings.IsStartupRegistered();
        _suppressEvents = false;

        // Selecting before the ComboBox is loaded can leave it blank
        LanguageBox.Loaded += (_, _) =>
        {
            _suppressEvents = true;
            LanguageBox.SelectedIndex = _settings.IsTurkish ? 0 : 1;
            _suppressEvents = false;
        };

        ApplyLanguage();
        Tick();

        _ = _service.CheckAsync(_service.AutoPatchEnabled);

        _ticker.Tick += (_, _) => Tick();
        _ticker.Start();
    }

    private bool Tr => _settings.IsTurkish;

    private string T(string turkish, string english) => Tr ? turkish : english;

    // ---------------------------------------------------------------- window

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private IntPtr Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);

    private void ConfigureAppWindow()
    {
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
        AppWindow.Title = "Bongo Cat AutoClaim";

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }

        double scale = GetDpiForWindow(Hwnd) / 96.0;
        int w = (int)(WindowWidth * scale);
        int h = (int)(WindowHeight * scale);
        DisplayArea area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        h = Math.Min(h, area.WorkArea.Height - (int)(32 * scale));
        AppWindow.MoveAndResize(new RectInt32(
            area.WorkArea.X + (area.WorkArea.Width - w) / 2,
            area.WorkArea.Y + (area.WorkArea.Height - h) / 2,
            w, h));

        // The close button only hides the window; the app keeps running in the tray.
        // Quitting is done from the tray menu or the Quit link.
        AppWindow.Closing += (_, e) =>
        {
            if (_allowClose)
            {
                Shutdown();
                return;
            }
            e.Cancel = true;
            AppWindow.Hide();
            ShowTrayHintOnce();
        };
    }

    public void ShowAndActivate()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter p && p.State == OverlappedPresenterState.Minimized)
        {
            p.Restore();
        }
        Activate();
    }

    private void Shutdown()
    {
        _ticker.Stop();
        _tray.Dispose();
        _service.Dispose();
    }

    private void ShowTrayHintOnce()
    {
        if (_trayHintShown) return;
        _trayHintShown = true;
        try
        {
            _tray.ShowNotification(
                "Bongo Cat AutoClaim",
                T("Arka planda çalışmaya devam ediyor. Tepsi ikonundan açabilir ya da çıkabilirsin.",
                  "Still running in the background. Use the tray icon to open or quit."));
        }
        catch { }
    }

    private void Exit()
    {
        _allowClose = true;
        Close();
        Application.Current.Exit();
    }

    // ---------------------------------------------------------------- tray

    private TaskbarIcon CreateTrayIcon()
    {
        var tray = new TaskbarIcon
        {
            ToolTipText = "Bongo Cat AutoClaim",
            IconSource = new BitmapImage(new Uri("ms-appx:///Assets/app.ico")),
            // Native Win32 menu. SecondWindow mode crashed after a GC (H.NotifyIcon subclass delegate)
            ContextMenuMode = ContextMenuMode.PopupMenu,
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(ShowAndActivate),
            ContextFlyout = BuildTrayMenu()
        };
        tray.ForceCreate(enablesEfficiencyMode: false);
        return tray;
    }

    private MenuFlyout BuildTrayMenu()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = T("Aç", "Open"), Command = new RelayCommand(ShowAndActivate) });
        menu.Items.Add(new MenuFlyoutSeparator());
        _autoClaimItem = new ToggleMenuFlyoutItem
        {
            Text = T("Otomatik toplama", "Auto-claim"),
            IsChecked = _service.State == PatchState.Patched
        };
        _autoClaimItem.Command = new RelayCommand(() => SetAutoClaim(_service.State != PatchState.Patched));
        menu.Items.Add(_autoClaimItem);
        menu.Items.Add(new MenuFlyoutItem { Text = T("Bongo Cat'i yeniden başlat", "Restart Bongo Cat"), Command = new RelayCommand(RestartGame) });
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = T("Tamamen kapat", "Quit"), Command = new RelayCommand(Exit) });
        return menu;
    }

    /// <summary>On: apply the patch and keep it after updates. Off: restore the original file.</summary>
    private void SetAutoClaim(bool on)
    {
        if (on)
        {
            _settings.AutoPatch = true;
            _settings.Save();
            _service.AutoPatchEnabled = true;
            _suppressEvents = true;
            AutoPatchToggle.IsOn = true;
            _suppressEvents = false;
            _ = _service.ApplyAsync(manual: true);
        }
        else
        {
            _ = _service.RestoreAsync();
        }
    }

    private void OnQuitClick(object sender, RoutedEventArgs e) => Exit();

    // ---------------------------------------------------------------- language

    private void ApplyLanguage()
    {
        SettingsHeader.Text = T("Ayarlar", "Settings");
        CosmeticTitle.Text = T("Kozmetik sandık", "Cosmetic chest");
        EmoteTitle.Text = T("Emote sandığı", "Emote chest");

        AutoPatchCard.Header = T("Güncellemelerden sonra yeniden yamala", "Re-patch after Steam updates");
        AutoPatchCard.Description = T("Steam oyunu güncellediğinde yama otomatik geri gelir.", "Puts the patch back after every game update.");
        StartupCard.Header = T("Windows ile başlat", "Start with Windows");
        StartupCard.Description = T("Sistem tepsisinde sessizce çalışır.", "Runs quietly in the system tray.");
        FolderCard.Header = T("Oyun klasörü", "Game folder");
        BrowseButton.Content = T("Değiştir", "Change");
        LanguageCard.Header = T("Dil", "Language");
        RestoreButton.Content = T("Orijinale dön", "Restore original");
        QuitLink.Content = T("Tamamen kapat", "Quit");

        _tray.ContextFlyout = BuildTrayMenu();
        UpdateFolder();
        UpdateStatus();
        Tick();
    }

    private void UpdateFolder()
    {
        FolderCard.Description = string.IsNullOrEmpty(_service.GameDir)
            ? T("Bongo Cat klasörü bulunamadı", "Bongo Cat folder not found")
            : _service.GameDir;
    }

    // ---------------------------------------------------------------- status

    private Brush Res(string key) => (Brush)Application.Current.Resources[key];

    private void UpdateStatus()
    {
        bool hasBackup = GameFiles.HasBackup(_service.GameDir);
        BackupText.Text = hasBackup ? T("Yedek alındı", "Backup saved") : T("Henüz yedek yok", "No backup yet");
        ToolTipService.SetToolTip(BackupText, "Assembly-CSharp.dll · SHA256 " + _service.LastHash);
        RestoreButton.IsEnabled = hasBackup;

        string title, body, glyph, fg, bg;
        switch (_service.State)
        {
            case PatchState.Patched:
                title = T("Otomatik toplama açık", "Auto-claim is on");
                body = T("Sayaç 00:00 olduğunda sandık anında alınır (1.000 vuruş gerekir).",
                         "Chests are claimed the moment their timer hits 00:00 (1,000 taps needed).");
                glyph = "";
                fg = "SystemFillColorSuccessBrush";
                bg = "SystemFillColorSuccessBackgroundBrush";
                break;
            case PatchState.Unpatched:
                title = T("Otomatik toplama kapalı", "Auto-claim is off");
                body = T("Açtığında sandıklara artık tıklaman gerekmez.",
                         "Turn it on and you won't have to click chests anymore.");
                glyph = "";
                fg = "SystemFillColorCautionBrush";
                bg = "SystemFillColorCautionBackgroundBrush";
                break;
            case PatchState.GameNotFound:
                title = T("Bongo Cat bulunamadı", "Bongo Cat not found");
                body = T("Ayarlardan oyun klasörünü seç.", "Pick your game folder in Settings below.");
                glyph = "";
                fg = "SystemFillColorCriticalBrush";
                bg = "SystemFillColorCriticalBackgroundBrush";
                break;
            case PatchState.Error:
                title = T("Oyun kontrol edilemedi", "Couldn't check the game");
                body = T("Alttaki mesaja bak. Oyunu yeniden başlatmak işe yarayabilir.",
                         "See the message at the bottom. Restarting the game may help.");
                glyph = "";
                fg = "SystemFillColorCriticalBrush";
                bg = "SystemFillColorCriticalBackgroundBrush";
                break;
            default:
                title = T("Kontrol ediliyor…", "Checking…");
                body = T("Oyun dosyalarına bakılıyor.", "Looking at your game files.");
                glyph = "";
                fg = "TextFillColorSecondaryBrush";
                bg = "SubtleFillColorSecondaryBrush";
                break;
        }

        StatusTitle.Text = title;
        StatusBody.Text = body;
        StatusIcon.Glyph = glyph;
        StatusIcon.Foreground = Res(fg);
        StatusBadge.Background = Res(bg);

        PatchButton.Content = _service.State == PatchState.Patched
            ? T("Yamayı doğrula", "Verify patch")
            : T("Otomatik toplamayı aç", "Turn on auto-claim");
        PatchButton.IsEnabled = _service.State != PatchState.GameNotFound;
        if (_autoClaimItem != null)
        {
            _autoClaimItem.IsChecked = _service.State == PatchState.Patched;
            _autoClaimItem.IsEnabled = _service.State is PatchState.Patched or PatchState.Unpatched;
        }
    }

    private void Tick()
    {
        bool running = GameFiles.IsGameRunning();
        GameDot.Fill = Res(running ? "SystemFillColorSuccessBrush" : "TextFillColorTertiaryBrush");
        GameText.Text = running ? T("Oyun çalışıyor", "Game running") : T("Oyun çalışmıyor", "Game not running");
        GameButton.Content = running ? T("Yeniden başlat", "Restart game") : T("Oyunu başlat", "Launch game");
        GameButton.IsEnabled = !string.IsNullOrEmpty(_service.GameDir);

        var (cosmetic, emote) = ChestTimers.Read();
        UpdateTimer(CosmeticRing, CosmeticTime, CosmeticCaption, cosmetic);
        UpdateTimer(EmoteRing, EmoteTime, EmoteCaption, emote);

        if (DateTime.Now.Second % 30 == 0 && _service.GameDir != null && !_service.IsBusy)
        {
            _ = _service.CheckAsync(_service.AutoPatchEnabled);
        }
    }

    private void UpdateTimer(ProgressRing ring, TextBlock time, TextBlock caption, int secondsLeft)
    {
        if (secondsLeft < 0)
        {
            ring.Value = 0;
            time.Text = "--:--";
            time.Foreground = Res("TextFillColorTertiaryBrush");
            caption.Text = T("Henüz sayaç verisi yok", "No timer data yet");
            return;
        }

        int clamped = Math.Clamp(secondsLeft, 0, ChestTimers.CycleSeconds);
        ring.Value = ChestTimers.CycleSeconds - clamped;
        if (clamped == 0)
        {
            ring.Foreground = Res("SystemFillColorSuccessBrush");
            time.Text = "00:00";
            time.Foreground = Res("SystemFillColorSuccessBrush");
            caption.Text = T("Alınmaya hazır", "Ready to claim");
            return;
        }

        ring.Foreground = Res("AccentFillColorDefaultBrush");
        time.Text = TimeSpan.FromSeconds(clamped).ToString(@"mm\:ss");
        time.Foreground = Res("TextFillColorPrimaryBrush");
        caption.Text = T("sonraki sandığa", "until next chest");
    }

    private void OnNotice(Notice n)
    {
        string text = n.Kind switch
        {
            NoticeKind.Verified => T("Yama doğrulandı.", "Patch verified."),
            NoticeKind.UpdateDetected => T("Güncelleme algılandı, yama uygulanıyor…", "Game update detected, applying patch…"),
            NoticeKind.Applied => T("Yama uygulandı", "Patch applied") + " (" + n.Detail + ")",
            NoticeKind.AlreadyActive => T("Yama zaten aktif.", "Patch is already active."),
            NoticeKind.Restored => T("Orijinal dosya geri yüklendi.", "Original file restored."),
            NoticeKind.NoBackup => T("Yedek dosyası bulunamadı.", "Backup file not found."),
            NoticeKind.ReadFailed => T("Hata: oyun dosyası okunamadı.", "Error: couldn't read the game file."),
            _ => T("Hata: ", "Error: ") + n.Detail
        };

        if (n.Kind == NoticeKind.Restored)
        {
            // The service turned auto re-patch off before restoring
            _settings.AutoPatch = false;
            _settings.Save();
            _suppressEvents = true;
            AutoPatchToggle.IsOn = false;
            _suppressEvents = false;
        }

        SetActivity(text);
        UpdateStatus();
    }

    private void SetActivity(string text)
    {
        _lastActivity = text;
        ActivityText.Text = $"{DateTime.Now:HH:mm}  ·  {text}";
        ToolTipService.SetToolTip(ActivityText, text);
        Log.Write(text);
    }

    // ---------------------------------------------------------------- handlers

    private void OnGameButtonClick(object sender, RoutedEventArgs e) => RestartGame();

    private void RestartGame()
    {
        string? dir = _service.GameDir;
        _ = Task.Run(() =>
        {
            GameFiles.StopGame();
            Thread.Sleep(350);
            GameFiles.LaunchGame(dir);
        });
    }

    private void OnPatchClick(object sender, RoutedEventArgs e) => _ = _service.ApplyAsync(manual: true);

    private void OnRestoreClick(object sender, RoutedEventArgs e) => _ = _service.RestoreAsync();

    private void OnAutoPatchToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        _settings.AutoPatch = AutoPatchToggle.IsOn;
        _service.AutoPatchEnabled = AutoPatchToggle.IsOn;
        _settings.Save();
        SetActivity(AutoPatchToggle.IsOn
            ? T("Güncelleme koruması açıldı.", "Update watcher turned on.")
            : T("Güncelleme koruması kapatıldı.", "Update watcher turned off."));
        if (AutoPatchToggle.IsOn)
        {
            _ = _service.CheckAsync(true);
        }
    }

    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        Settings.SetStartupRegistration(StartupToggle.IsOn);
        SetActivity(StartupToggle.IsOn
            ? T("Windows ile başlatma açıldı.", "Will start with Windows.")
            : T("Windows ile başlatma kapatıldı.", "Won't start with Windows."));
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        _settings.IsTurkish = LanguageBox.SelectedIndex == 0;
        _settings.Save();
        ApplyLanguage();
    }

    private async void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder == null) return;

        if (!SteamLocator.IsValidBongoCatDirectory(folder.Path))
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = T("Bu klasör Bongo Cat değil", "That's not the Bongo Cat folder"),
                Content = T("Seçilen klasörde BongoCat_Data\\Managed\\Assembly-CSharp.dll bulunamadı. Genelde Steam\\steamapps\\common\\BongoCat altındadır.",
                            "BongoCat_Data\\Managed\\Assembly-CSharp.dll wasn't found there. It's usually under Steam\\steamapps\\common\\BongoCat."),
                CloseButtonText = T("Tamam", "OK"),
                DefaultButton = ContentDialogButton.Close
            };
            await dialog.ShowAsync();
            return;
        }

        _settings.GameDirectory = folder.Path;
        _settings.Save();
        _service.SetGameDirectory(folder.Path);
        UpdateFolder();
        _ = _service.CheckAsync(false);
    }
}

internal sealed class RelayCommand : ICommand
{
    private readonly Action _execute;

    public RelayCommand(Action execute) => _execute = execute;

    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _execute();
}
