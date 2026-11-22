using Microsoft.UI.Xaml;

namespace BongoCatAutoClaim;

public partial class App : Application
{
    private readonly bool _startMinimized;
    private readonly EventWaitHandle _showEvent;
    private MainWindow? _window;

    public App(bool startMinimized, EventWaitHandle showEvent)
    {
        _startMinimized = startMinimized;
        _showEvent = showEvent;
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        if (!_startMinimized)
        {
            _window.ShowAndActivate();
        }

        // A second launch signals this event; bring the window back
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    _showEvent.WaitOne();
                    _window.DispatcherQueue.TryEnqueue(() => _window.ShowAndActivate());
                }
                catch
                {
                    break;
                }
            }
        })
        {
            IsBackground = true
        };
        thread.Start();
    }
}
