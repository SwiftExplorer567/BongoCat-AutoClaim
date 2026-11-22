namespace BongoCatAutoClaim.Core;

public static class Log
{
    private static readonly object Gate = new();

    /// <summary>patcher.log sits next to the exe, same as earlier versions.</summary>
    private static string FilePath
    {
        get
        {
            string? exe = Environment.ProcessPath;
            string dir = exe != null ? Path.GetDirectoryName(exe)! : AppContext.BaseDirectory;
            return Path.Combine(dir, "patcher.log");
        }
    }

    public static void Write(string text)
    {
        try
        {
            lock (Gate)
            {
                File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
