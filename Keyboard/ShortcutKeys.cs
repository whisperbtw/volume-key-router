using System.Windows.Forms;

namespace VolumeKeyRouter;

internal static class KeyboardShortcutKeys
{
    private const int MaxVirtualKey = 0xFE;
    public const int None = 0;
    public const int F1 = 0x70;
    public const int F2 = 0x71;
    public const int F3 = 0x72;
    public const int F4 = 0x73;
    public const int F5 = 0x74;
    public const int F6 = 0x75;
    public const int F7 = 0x76;
    public const int F8 = 0x77;
    public const int F9 = 0x78;
    public const int F10 = 0x79;
    public const int F11 = 0x7A;
    public const int F12 = 0x7B;
    public const int VolumeMute = 0xAD;
    public const int VolumeDown = 0xAE;
    public const int VolumeUp = 0xAF;
    public const int MediaNextTrack = 0xB0;
    public const int MediaPreviousTrack = 0xB1;
    public const int MediaStop = 0xB2;
    public const int MediaPlayPause = 0xB3;
    public const int LaunchMediaSelect = 0xB5;

    private static readonly ShortcutKeyOption[] Options =
    {
        new(None, "Desativado"),
        new(VolumeDown, "Volume -"),
        new(VolumeUp, "Volume +"),
        new(VolumeMute, "Mute"),
        new(LaunchMediaSelect, "Midia / Fn+F1"),
        new(MediaPreviousTrack, "Midia anterior"),
        new(MediaNextTrack, "Proxima midia"),
        new(MediaPlayPause, "Play/Pause"),
        new(MediaStop, "Parar midia"),
        new(F1, "F1"),
        new(F2, "F2"),
        new(F3, "F3"),
        new(F4, "F4"),
        new(F5, "F5"),
        new(F6, "F6"),
        new(F7, "F7"),
        new(F8, "F8"),
        new(F9, "F9"),
        new(F10, "F10"),
        new(F11, "F11"),
        new(F12, "F12")
    };

    public static IReadOnlyList<ShortcutKeyOption> AllOptions => Options;

    public static int Normalize(int virtualKeyCode, int fallback)
    {
        if (virtualKeyCode == None)
        {
            return None;
        }

        return virtualKeyCode is > None and <= MaxVirtualKey
            ? virtualKeyCode
            : fallback;
    }

    public static string GetDisplayName(int virtualKeyCode)
    {
        var knownName = Options.FirstOrDefault(option => option.VirtualKeyCode == virtualKeyCode)?.DisplayName;
        if (!string.IsNullOrWhiteSpace(knownName))
        {
            return knownName;
        }

        return Enum.IsDefined(typeof(Keys), virtualKeyCode)
            ? ((Keys)virtualKeyCode).ToString()
            : $"VK {virtualKeyCode}";
    }

    public static string GetDisplayName(ShortcutBinding binding)
    {
        if (!binding.IsConfigured)
        {
            return GetDisplayName(None);
        }

        var parts = new List<string>();
        if (binding.Modifiers.HasFlag(ShortcutModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (binding.Modifiers.HasFlag(ShortcutModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (binding.Modifiers.HasFlag(ShortcutModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (binding.Modifiers.HasFlag(ShortcutModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(GetDisplayName(binding.VirtualKeyCode));
        return string.Join(" + ", parts);
    }

    public static ShortcutModifiers NormalizeModifiers(ShortcutModifiers modifiers)
    {
        return modifiers & (
            ShortcutModifiers.Control |
            ShortcutModifiers.Shift |
            ShortcutModifiers.Alt |
            ShortcutModifiers.Win);
    }
}

internal sealed record ShortcutKeyOption(int VirtualKeyCode, string DisplayName)
{
    public override string ToString()
    {
        return DisplayName;
    }
}

internal readonly record struct ShortcutBinding(int VirtualKeyCode, ShortcutModifiers Modifiers)
{
    public bool IsConfigured => VirtualKeyCode != KeyboardShortcutKeys.None;

    public ShortcutBinding Normalize(int fallback)
    {
        var key = KeyboardShortcutKeys.Normalize(VirtualKeyCode, fallback);
        var modifiers = key == KeyboardShortcutKeys.None
            ? ShortcutModifiers.None
            : KeyboardShortcutKeys.NormalizeModifiers(Modifiers);
        return new ShortcutBinding(key, modifiers);
    }

    public override string ToString()
    {
        return KeyboardShortcutKeys.GetDisplayName(this);
    }
}
