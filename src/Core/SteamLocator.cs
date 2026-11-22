using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BongoCatAutoClaim.Core;

public static class SteamLocator
{
    public static string? FindBongoCatDirectory()
    {
        var candidates = new List<string>();

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string steamPath && steamPath.Length > 0)
            {
                steamPath = steamPath.Replace('/', '\\');
                candidates.Add(Path.Combine(steamPath, @"steamapps\common\BongoCat"));
                AddVdfLibraries(Path.Combine(steamPath, @"steamapps\libraryfolders.vdf"), candidates);
            }
        }
        catch { }

        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            if (key?.GetValue("InstallPath") is string installPath && installPath.Length > 0)
            {
                candidates.Add(Path.Combine(installPath, @"steamapps\common\BongoCat"));
                AddVdfLibraries(Path.Combine(installPath, @"steamapps\libraryfolders.vdf"), candidates);
            }
        }
        catch { }

        candidates.AddRange(new[]
        {
            @"C:\Program Files (x86)\Steam\steamapps\common\BongoCat",
            @"C:\Program Files\Steam\steamapps\common\BongoCat",
            @"D:\SteamLibrary\steamapps\common\BongoCat",
            @"D:\Steam\steamapps\common\BongoCat",
            @"E:\SteamLibrary\steamapps\common\BongoCat",
            @"F:\SteamLibrary\steamapps\common\BongoCat"
        });

        return candidates.FirstOrDefault(IsValidBongoCatDirectory);
    }

    private static void AddVdfLibraries(string vdfPath, List<string> candidates)
    {
        try
        {
            if (!File.Exists(vdfPath)) return;
            string content = File.ReadAllText(vdfPath);
            foreach (Match m in Regex.Matches(content, "\"path\"\\s+\"([^\"]+)\""))
            {
                string libPath = m.Groups[1].Value.Replace("\\\\", "\\");
                candidates.Add(Path.Combine(libPath, @"steamapps\common\BongoCat"));
            }
        }
        catch { }
    }

    public static bool IsValidBongoCatDirectory(string? dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
        return File.Exists(Path.Combine(dir, @"BongoCat_Data\Managed\Assembly-CSharp.dll"));
    }
}
