namespace BongoCatAutoClaim.Core;

public enum PatchState
{
    Checking,
    GameNotFound,
    Unpatched,
    Patched,
    Error
}

public enum NoticeKind
{
    Verified,
    UpdateDetected,
    Applied,
    AlreadyActive,
    Restored,
    NoBackup,
    ReadFailed,
    Error
}

public sealed record Notice(NoticeKind Kind, string Detail = "");

/// <summary>
/// Checks, applies and restores the patch, and watches Assembly-CSharp.dll for Steam updates.
/// Work runs on the thread pool; events are raised from background threads.
/// </summary>
public sealed class PatchService : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FileSystemWatcher? _watcher;
    private volatile bool _autoPatchEnabled = true;

    public string? GameDir { get; private set; }
    public PatchState State { get; private set; } = PatchState.Checking;
    public string LastHash { get; private set; } = "--";
    public bool IsBusy => _gate.CurrentCount == 0;

    public bool AutoPatchEnabled
    {
        get => _autoPatchEnabled;
        set => _autoPatchEnabled = value;
    }

    public event Action? StateChanged;
    public event Action<Notice>? NoticeRaised;

    public void SetGameDirectory(string? gameDir)
    {
        GameDir = gameDir;
        LastHash = "--";
        _cachedHash = null;
        Watch(gameDir);
    }

    public Task CheckAsync(bool autoPatch) => Task.Run(() =>
    {
        _gate.Wait();
        try
        {
            CheckCore(autoPatch);
        }
        finally
        {
            _gate.Release();
        }
    });

    public Task ApplyAsync(bool manual)
    {
        if (IsBusy) return Task.CompletedTask;
        return Task.Run(() =>
        {
            _gate.Wait();
            try
            {
                ApplyCore(manual);
            }
            finally
            {
                _gate.Release();
            }
        });
    }

    public Task RestoreAsync()
    {
        if (IsBusy) return Task.CompletedTask;
        return Task.Run(() =>
        {
            _gate.Wait();
            try
            {
                RestoreCore();
            }
            finally
            {
                _gate.Release();
            }
        });
    }

    private string? _cachedHash;

    private void CheckCore(bool autoPatch)
    {
        string? dir = GameDir;
        if (!SteamLocator.IsValidBongoCatDirectory(dir))
        {
            SetState(PatchState.GameNotFound, "--");
            return;
        }

        byte[]? raw = GameFiles.ReadWithRetry(GameFiles.DllPath(dir!), 10, 300);
        if (raw == null)
        {
            SetState(PatchState.Error, "LOCKED");
            return;
        }

        string hash = GameFiles.ShortHash(raw);
        if (hash == _cachedHash)
        {
            StateChanged?.Invoke();
            return;
        }

        PatchResult check = PatchEngine.Analyze(raw, inspectOnly: true);
        _cachedHash = hash;
        if (!check.Success)
        {
            SetState(PatchState.Error, hash);
            Raise(NoticeKind.Error, check.Message);
            return;
        }

        if (check.AlreadyPatched)
        {
            SetState(PatchState.Patched, hash);
            Raise(NoticeKind.Verified);
            return;
        }

        if (autoPatch)
        {
            State = PatchState.Unpatched;
            LastHash = hash;
            Raise(NoticeKind.UpdateDetected);
            ApplyCore(manual: false);
            return;
        }

        SetState(PatchState.Unpatched, hash);
    }

    private void ApplyCore(bool manual)
    {
        string? dir = GameDir;
        if (!SteamLocator.IsValidBongoCatDirectory(dir)) return;

        try
        {
            byte[]? raw = GameFiles.ReadWithRetry(GameFiles.DllPath(dir!), 12, 350);
            if (raw == null)
            {
                Raise(NoticeKind.ReadFailed);
                return;
            }

            PatchResult res = PatchEngine.Analyze(raw, inspectOnly: false);
            if (!res.Success)
            {
                Raise(NoticeKind.Error, "Patch error: " + res.Message);
                return;
            }

            if (res.AlreadyPatched)
            {
                _cachedHash = GameFiles.ShortHash(raw);
                SetState(PatchState.Patched, _cachedHash);
                if (manual) Raise(NoticeKind.AlreadyActive);
                return;
            }

            // Re-read the built image before touching disk
            PatchResult verify = PatchEngine.Analyze(res.PatchedBytes!, inspectOnly: true);
            if (!verify.Success || !verify.AlreadyPatched)
            {
                Raise(NoticeKind.Error, "Verification failed on constructed patch.");
                return;
            }

            PauseWatcher(true);
            File.WriteAllBytes(GameFiles.BackupPath(dir!), raw);
            File.WriteAllBytes(GameFiles.TempPath(dir!), res.PatchedBytes!);

            bool wasRunning = GameFiles.StopGame();
            File.Copy(GameFiles.TempPath(dir!), GameFiles.DllPath(dir!), overwrite: true);
            File.Delete(GameFiles.TempPath(dir!));

            _cachedHash = GameFiles.ShortHash(res.PatchedBytes!);
            if (wasRunning) GameFiles.LaunchGame(dir);

            SetState(PatchState.Patched, _cachedHash);
            Raise(NoticeKind.Applied, _cachedHash);
        }
        catch (Exception ex)
        {
            Raise(NoticeKind.Error, ex.Message);
        }
        finally
        {
            PauseWatcher(false);
        }
    }

    private void RestoreCore()
    {
        string? dir = GameDir;
        if (!SteamLocator.IsValidBongoCatDirectory(dir)) return;

        try
        {
            if (!File.Exists(GameFiles.BackupPath(dir!)))
            {
                Raise(NoticeKind.NoBackup);
                return;
            }

            PauseWatcher(true);
            // Turn the watcher's auto re-patch off first so it doesn't undo the restore
            _autoPatchEnabled = false;

            bool wasRunning = GameFiles.StopGame();
            File.Copy(GameFiles.BackupPath(dir!), GameFiles.DllPath(dir!), overwrite: true);

            _cachedHash = GameFiles.ShortHash(File.ReadAllBytes(GameFiles.DllPath(dir!)));
            if (wasRunning) GameFiles.LaunchGame(dir);

            SetState(PatchState.Unpatched, _cachedHash);
            Raise(NoticeKind.Restored);
        }
        catch (Exception ex)
        {
            Raise(NoticeKind.Error, "Restore error: " + ex.Message);
        }
        finally
        {
            PauseWatcher(false);
        }
    }

    private void Watch(string? gameDir)
    {
        try
        {
            _watcher?.Dispose();
            _watcher = null;
            if (string.IsNullOrEmpty(gameDir)) return;

            string managedDir = GameFiles.ManagedDir(gameDir);
            if (!Directory.Exists(managedDir)) return;

            _watcher = new FileSystemWatcher(managedDir, "Assembly-CSharp.dll")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime
            };
            FileSystemEventHandler onChange = (_, _) => ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(900);
                _cachedHash = null;
                _ = CheckAsync(_autoPatchEnabled);
            });
            _watcher.Changed += onChange;
            _watcher.Created += onChange;
            _watcher.Renamed += (s, e) => onChange(s, e);
            _watcher.EnableRaisingEvents = true;
        }
        catch { }
    }

    private void PauseWatcher(bool pause)
    {
        try
        {
            if (_watcher != null) _watcher.EnableRaisingEvents = !pause;
        }
        catch { }
    }

    private void SetState(PatchState state, string hash)
    {
        State = state;
        LastHash = hash;
        StateChanged?.Invoke();
    }

    private void Raise(NoticeKind kind, string detail = "") => NoticeRaised?.Invoke(new Notice(kind, detail));

    public void Dispose()
    {
        _watcher?.Dispose();
        _gate.Dispose();
    }
}
