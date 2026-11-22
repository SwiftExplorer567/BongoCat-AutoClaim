using System.Globalization;
using Microsoft.Win32;

namespace BongoCatAutoClaim.Core;

/// <summary>User preferences stored under HKCU\Software\BongoCatAutoClaim (same keys as earlier versions).</summary>
public sealed class Settings
{
    private const string KeyPath = @"Software\BongoCatAutoClaim";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "BongoCatAutoClaim";
    private const string LegacyRunValueName = "BongoCatAutoPatcher";

    public bool IsTurkish { get; set; }
    public bool AutoPatch { get; set; } = true;
    public string? GameDirectory { get; set; }

    public static Settings Load()
    {
        var s = new Settings
        {
            IsTurkish = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase)
        };

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath, false);
            if (key != null)
            {
                if (key.GetValue("Language") is string lang)
                {
                    s.IsTurkish = string.Equals(lang, "tr", StringComparison.OrdinalIgnoreCase);
                }
                if (key.GetValue("AutoPatch") is object auto)
                {
                    s.AutoPatch = Convert.ToInt32(auto) != 0;
                }
                if (key.GetValue("GameDirectory") is string dir && SteamLocator.IsValidBongoCatDirectory(dir))
                {
                    s.GameDirectory = dir;
                }
            }
        }
        catch { }

        return s;
    }

    public void Save()
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key.SetValue("Language", IsTurkish ? "tr" : "en");
            key.SetValue("AutoPatch", AutoPatch ? 1 : 0);
            if (!string.IsNullOrEmpty(GameDirectory))
            {
                key.SetValue("GameDirectory", GameDirectory);
            }
        }
        catch { }
    }

    public static bool IsStartupRegistered()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return !string.IsNullOrEmpty(key?.GetValue(RunValueName) as string);
        }
        catch
        {
            return false;
        }
    }

    public static void SetStartupRegistration(bool enable)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null) return;
            if (enable)
            {
                string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "BongoCatAutoClaim.exe");
                key.SetValue(RunValueName, "\"" + exe + "\" --minimized");
                if (key.GetValue(LegacyRunValueName) != null)
                {
                    key.DeleteValue(LegacyRunValueName, false);
                }
            }
            else
            {
                key.DeleteValue(RunValueName, false);
            }
        }
        catch { }
    }
}
