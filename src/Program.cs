using BongoCatAutoClaim.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace BongoCatAutoClaim;

public static class Program
{
    // Same names as earlier versions, so old and new builds never run side by side
    private const string MutexName = @"Local\BongoCatAutoClaimSingleton";
    private const string ShowEventName = @"Local\BongoCatAutoClaimShowEvent";

    [STAThread]
    private static int Main(string[] args)
    {
        bool startMinimized = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        if (args.Any(a => a.Equals("--once", StringComparison.OrdinalIgnoreCase)))
        {
            RunHeadlessOnce();
            return 0;
        }

        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Bring the running instance to the front instead
            try
            {
                using EventWaitHandle evt = EventWaitHandle.OpenExisting(ShowEventName);
                evt.Set();
            }
            catch { }
            return 0;
        }

        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(callbackParams =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App(startMinimized, showEvent);
        });
        return 0;
    }

    private static void RunHeadlessOnce()
    {
        string? gameDir = SteamLocator.FindBongoCatDirectory();
        if (!SteamLocator.IsValidBongoCatDirectory(gameDir)) return;

        byte[] raw = File.ReadAllBytes(GameFiles.DllPath(gameDir!));
        PatchResult res = PatchEngine.Analyze(raw, inspectOnly: false);
        if (res.Success && !res.AlreadyPatched && res.PatchedBytes != null)
        {
            File.WriteAllBytes(GameFiles.BackupPath(gameDir!), raw);
            File.WriteAllBytes(GameFiles.DllPath(gameDir!), res.PatchedBytes);
        }
    }
}
