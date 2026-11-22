using Microsoft.Win32;

namespace BongoCatAutoClaim.Core;

/// <summary>Reads the chest countdowns Bongo Cat stores in its Unity PlayerPrefs.</summary>
public static class ChestTimers
{
    public const int CycleSeconds = 1800;

    /// <summary>Seconds left for each chest, or -1 when the game hasn't written a value yet.</summary>
    public static (int Cosmetic, int Emote) Read()
    {
        int cosmetic = -1;
        int emote = -1;
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Irox Games\BongoCat");
            if (key != null)
            {
                foreach (string name in key.GetValueNames())
                {
                    if (name.StartsWith("TIME_LEFT_h", StringComparison.OrdinalIgnoreCase))
                    {
                        cosmetic = Convert.ToInt32(key.GetValue(name));
                    }
                    else if (name.StartsWith("EMOTE_CHEST_TIME_LEFT_h", StringComparison.OrdinalIgnoreCase))
                    {
                        emote = Convert.ToInt32(key.GetValue(name));
                    }
                }
            }
        }
        catch { }
        return (cosmetic, emote);
    }
}
