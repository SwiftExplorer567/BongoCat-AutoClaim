using System.Diagnostics;
using System.Security.Cryptography;

namespace BongoCatAutoClaim.Core;

/// <summary>File and process helpers for the Bongo Cat install.</summary>
public static class GameFiles
{
    public static string ManagedDir(string gameDir) => Path.Combine(gameDir, @"BongoCat_Data\Managed");
    public static string DllPath(string gameDir) => Path.Combine(ManagedDir(gameDir), "Assembly-CSharp.dll");
    public static string BackupPath(string gameDir) => Path.Combine(ManagedDir(gameDir), "Assembly-CSharp.dll.bak");
    public static string TempPath(string gameDir) => Path.Combine(ManagedDir(gameDir), "Assembly-CSharp.dll.tmp");

    public static bool HasBackup(string? gameDir) =>
        !string.IsNullOrEmpty(gameDir) && File.Exists(BackupPath(gameDir));

    public static byte[]? ReadWithRetry(string path, int maxAttempts, int delayMs)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                byte[] data = new byte[fs.Length];
                fs.ReadExactly(data);
                return data;
            }
            catch
            {
                Thread.Sleep(delayMs);
            }
        }
        return null;
    }

    public static string ShortHash(byte[] data) => Convert.ToHexString(SHA256.HashData(data))[..12];

    public static bool IsGameRunning()
    {
        Process[] procs = Process.GetProcessesByName("BongoCat");
        foreach (Process p in procs) p.Dispose();
        return procs.Length > 0;
    }

    /// <summary>Closes Bongo Cat (politely first). Returns true if it was running.</summary>
    public static bool StopGame()
    {
        Process[] procs = Process.GetProcessesByName("BongoCat");
        if (procs.Length == 0) return false;
        foreach (Process p in procs)
        {
            try
            {
                if (p.CloseMainWindow() && p.WaitForExit(1500)) continue;
                p.Kill();
                p.WaitForExit(4000);
            }
            catch { }
            finally
            {
                p.Dispose();
            }
        }
        return true;
    }

    public static void LaunchGame(string? gameDir)
    {
        try
        {
            if (string.IsNullOrEmpty(gameDir)) return;
            string exePath = Path.Combine(gameDir, "BongoCat.exe");
            if (!File.Exists(exePath)) return;
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = gameDir,
                UseShellExecute = true
            })?.Dispose();
        }
        catch { }
    }
}
