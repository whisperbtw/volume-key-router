using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VolumeKeyRouter;

internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string? LastDeviceId { get; set; }

    public string? LastDeviceName { get; set; }

    public bool FollowDefaultDevice { get; set; }

    public TargetMode TargetMode { get; set; } = TargetMode.Session;

    public string? LastSessionIdentifier { get; set; }

    public string? LastProcessName { get; set; }

    public int? LastProcessId { get; set; }

    public int StepPercent { get; set; } = 5;

    public bool CaptureActive { get; set; } = true;

    public bool MinimizeToTray { get; set; } = true;

    public bool StartMinimized { get; set; }

    public bool StartWithWindows { get; set; }

    public bool ShowVolumeOverlay { get; set; } = true;

    public OverlaySettings Overlay { get; set; } = new();

    public ShortcutSettings Shortcuts { get; set; } = new();

    public string? ActiveProfileId { get; set; }

    public List<ProfileSettings> Profiles { get; set; } = new();

    [JsonIgnore]
    public ProfileSettings? ActiveProfile =>
        Profiles.FirstOrDefault(profile => profile.Id == ActiveProfileId) ?? Profiles.FirstOrDefault();

    public static AppSettings Load()
    {
        var settings = TryLoad(SettingsPath);
        if (settings is not null)
        {
            return settings;
        }

        settings = TryLoad(BackupPath);
        if (settings is not null)
        {
            return settings;
        }

        TryPreserveCorruptFile(SettingsPath);
        settings = new AppSettings();
        settings.Normalize(applyActiveProfile: true);
        return settings;
    }

    private static AppSettings? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions)
                ?? new AppSettings();
            settings.Normalize(applyActiveProfile: true);
            return settings;
        }
        catch
        {
            TryPreserveCorruptFile(path);
            return null;
        }
    }

    private static void TryPreserveCorruptFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var target = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(path)}.corrupt-{stamp}.json");
            File.Copy(path, target, overwrite: true);
        }
        catch
        {
            // Best effort; never block startup because a backup failed.
        }
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Normalize(applyActiveProfile: false);
        var json = JsonSerializer.Serialize(this, JsonOptions);

        // Write to a temp file and move it into place atomically, so an
        // interrupted write can never leave the settings file corrupted.
        var tempPath = SettingsPath + ".tmp";
        File.WriteAllText(tempPath, json);
        if (File.Exists(SettingsPath))
        {
            File.Replace(tempPath, SettingsPath, null);
        }
        else
        {
            File.Move(tempPath, SettingsPath);
        }

        try
        {
            File.Copy(SettingsPath, BackupPath, overwrite: true);
        }
        catch
        {
            // Best effort; the live file is already in place.
        }
    }

    public void UpdateActiveProfileFromCurrent()
    {
        var profile = ActiveProfile;
        if (profile is null)
        {
            return;
        }

        profile.LastDeviceId = LastDeviceId;
        profile.LastDeviceName = LastDeviceName;
        profile.FollowDefaultDevice = FollowDefaultDevice;
        profile.TargetMode = TargetMode;
        profile.LastSessionIdentifier = LastSessionIdentifier;
        profile.LastProcessName = LastProcessName;
        profile.LastProcessId = LastProcessId;
        profile.StepPercent = StepPercent;
        profile.Shortcuts = Shortcuts.Clone();
        profile.Normalize();
    }

    public void ApplyActiveProfileToCurrent()
    {
        var profile = ActiveProfile;
        if (profile is null)
        {
            return;
        }

        LastDeviceId = profile.LastDeviceId;
        LastDeviceName = profile.LastDeviceName;
        FollowDefaultDevice = profile.FollowDefaultDevice;
        TargetMode = profile.TargetMode;
        LastSessionIdentifier = profile.LastSessionIdentifier;
        LastProcessName = profile.LastProcessName;
        LastProcessId = profile.LastProcessId;
        StepPercent = profile.StepPercent;
        Shortcuts = profile.Shortcuts.Clone();
        Shortcuts.Normalize();
    }

    public ProfileSettings AddProfileFromCurrent(string name)
    {
        Normalize(applyActiveProfile: false);
        var profile = ProfileSettings.FromCurrent(this, GetUniqueProfileName(name));
        Profiles.Add(profile);
        ActiveProfileId = profile.Id;
        ApplyActiveProfileToCurrent();
        return profile;
    }

    public bool RemoveActiveProfile()
    {
        var profile = ActiveProfile;
        if (profile is null || Profiles.Count <= 1)
        {
            return false;
        }

        Profiles.Remove(profile);
        ActiveProfileId = Profiles.First().Id;
        ApplyActiveProfileToCurrent();
        return true;
    }

    private void Normalize(bool applyActiveProfile)
    {
        StepPercent = Math.Clamp(StepPercent, 1, 50);
        Overlay ??= new OverlaySettings();
        Overlay.Normalize();
        Shortcuts ??= new ShortcutSettings();
        Shortcuts.Normalize();
        Profiles ??= new List<ProfileSettings>();
        Profiles.RemoveAll(profile => string.IsNullOrWhiteSpace(profile.Name));

        if (Profiles.Count == 0)
        {
            Profiles.Add(ProfileSettings.FromCurrent(this, "Padrao"));
        }

        foreach (var profile in Profiles)
        {
            profile.Normalize();
        }

        if (Profiles.All(profile => profile.Id != ActiveProfileId))
        {
            ActiveProfileId = Profiles.First().Id;
        }

        if (applyActiveProfile)
        {
            ApplyActiveProfileToCurrent();
        }
    }

    private string GetUniqueProfileName(string name)
    {
        var baseName = string.IsNullOrWhiteSpace(name) ? "Novo perfil" : name.Trim();
        var candidate = baseName;
        var suffix = 2;
        while (Profiles.Any(profile => profile.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName} {suffix}";
            suffix++;
        }

        return candidate;
    }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "volume-key-router",
        "settings.json");

    private static string BackupPath => SettingsPath + ".bak";
}

internal sealed class OverlaySettings
{
    public OverlayPosition Position { get; set; } = OverlayPosition.BottomCenter;

    public OverlaySizePreset SizePreset { get; set; } = OverlaySizePreset.Medium;

    public int Width { get; set; } = 430;

    public int Height { get; set; } = 136;

    public int DurationMs { get; set; } = 1200;

    public bool ShowArtwork { get; set; } = true;

    public OverlayTheme Theme { get; set; } = OverlayTheme.Dark;

    public void Normalize()
    {
        if (!Enum.IsDefined(SizePreset))
        {
            SizePreset = OverlaySizePreset.Medium;
        }

        var size = OverlaySizePresets.Get(SizePreset);
        Width = size.Width;
        Height = size.Height;
        DurationMs = Math.Clamp(DurationMs, 600, 5000);
        if (!Enum.IsDefined(Position))
        {
            Position = OverlayPosition.BottomCenter;
        }

        if (!Enum.IsDefined(Theme))
        {
            Theme = OverlayTheme.Dark;
        }
    }

    public OverlaySettings Clone()
    {
        return new OverlaySettings
        {
            Position = Position,
            SizePreset = SizePreset,
            Width = Width,
            Height = Height,
            DurationMs = DurationMs,
            ShowArtwork = ShowArtwork,
            Theme = Theme
        };
    }
}

internal static class OverlaySizePresets
{
    public static OverlaySize Get(OverlaySizePreset preset)
    {
        return preset switch
        {
            OverlaySizePreset.VerySmall => new OverlaySize(280, 86),
            OverlaySizePreset.Small => new OverlaySize(340, 108),
            OverlaySizePreset.Large => new OverlaySize(520, 164),
            _ => new OverlaySize(430, 136)
        };
    }
}

internal readonly record struct OverlaySize(int Width, int Height)
{
    public string DisplayText => $"{Width} x {Height}";
}

internal sealed class ShortcutSettings
{
    public int VolumeDownKey { get; set; } = KeyboardShortcutKeys.VolumeDown;

    public ShortcutModifiers VolumeDownModifiers { get; set; } = ShortcutModifiers.None;

    public int VolumeUpKey { get; set; } = KeyboardShortcutKeys.VolumeUp;

    public ShortcutModifiers VolumeUpModifiers { get; set; } = ShortcutModifiers.None;

    public int MuteKey { get; set; } = KeyboardShortcutKeys.VolumeMute;

    public ShortcutModifiers MuteModifiers { get; set; } = ShortcutModifiers.None;

    public int PeekMediaKey { get; set; } = KeyboardShortcutKeys.LaunchMediaSelect;

    public ShortcutModifiers PeekMediaModifiers { get; set; } = ShortcutModifiers.None;

    public int PreviousTrackKey { get; set; } = KeyboardShortcutKeys.MediaPreviousTrack;

    public ShortcutModifiers PreviousTrackModifiers { get; set; } = ShortcutModifiers.None;

    public int NextTrackKey { get; set; } = KeyboardShortcutKeys.MediaNextTrack;

    public ShortcutModifiers NextTrackModifiers { get; set; } = ShortcutModifiers.None;

    public int PlayPauseKey { get; set; } = KeyboardShortcutKeys.MediaPlayPause;

    public ShortcutModifiers PlayPauseModifiers { get; set; } = ShortcutModifiers.None;

    public int StopKey { get; set; } = KeyboardShortcutKeys.MediaStop;

    public ShortcutModifiers StopModifiers { get; set; } = ShortcutModifiers.None;

    public int LikeTrackKey { get; set; } = 0x4C; // L

    public ShortcutModifiers LikeTrackModifiers { get; set; } = ShortcutModifiers.Control | ShortcutModifiers.Alt;

    public bool ShowOverlayOnMediaKeys { get; set; } = true;

    [JsonIgnore]
    public ShortcutBinding VolumeDown => new(VolumeDownKey, VolumeDownModifiers);

    [JsonIgnore]
    public ShortcutBinding VolumeUp => new(VolumeUpKey, VolumeUpModifiers);

    [JsonIgnore]
    public ShortcutBinding Mute => new(MuteKey, MuteModifiers);

    [JsonIgnore]
    public ShortcutBinding PeekMedia => new(PeekMediaKey, PeekMediaModifiers);

    [JsonIgnore]
    public ShortcutBinding PreviousTrack => new(PreviousTrackKey, PreviousTrackModifiers);

    [JsonIgnore]
    public ShortcutBinding NextTrack => new(NextTrackKey, NextTrackModifiers);

    [JsonIgnore]
    public ShortcutBinding PlayPause => new(PlayPauseKey, PlayPauseModifiers);

    [JsonIgnore]
    public ShortcutBinding Stop => new(StopKey, StopModifiers);

    [JsonIgnore]
    public ShortcutBinding LikeTrack => new(LikeTrackKey, LikeTrackModifiers);

    public void Normalize()
    {
        ApplyVolumeDown(VolumeDown.Normalize(KeyboardShortcutKeys.VolumeDown));
        ApplyVolumeUp(VolumeUp.Normalize(KeyboardShortcutKeys.VolumeUp));
        ApplyMute(Mute.Normalize(KeyboardShortcutKeys.VolumeMute));
        ApplyPeekMedia(PeekMedia.Normalize(KeyboardShortcutKeys.LaunchMediaSelect));
        ApplyPreviousTrack(PreviousTrack.Normalize(KeyboardShortcutKeys.MediaPreviousTrack));
        ApplyNextTrack(NextTrack.Normalize(KeyboardShortcutKeys.MediaNextTrack));
        ApplyPlayPause(PlayPause.Normalize(KeyboardShortcutKeys.MediaPlayPause));
        ApplyStop(Stop.Normalize(KeyboardShortcutKeys.MediaStop));
        ApplyLikeTrack(LikeTrack.Normalize(0x4C));
    }

    public ShortcutSettings Clone()
    {
        return new ShortcutSettings
        {
            VolumeDownKey = VolumeDownKey,
            VolumeDownModifiers = VolumeDownModifiers,
            VolumeUpKey = VolumeUpKey,
            VolumeUpModifiers = VolumeUpModifiers,
            MuteKey = MuteKey,
            MuteModifiers = MuteModifiers,
            PeekMediaKey = PeekMediaKey,
            PeekMediaModifiers = PeekMediaModifiers,
            PreviousTrackKey = PreviousTrackKey,
            PreviousTrackModifiers = PreviousTrackModifiers,
            NextTrackKey = NextTrackKey,
            NextTrackModifiers = NextTrackModifiers,
            PlayPauseKey = PlayPauseKey,
            PlayPauseModifiers = PlayPauseModifiers,
            StopKey = StopKey,
            StopModifiers = StopModifiers,
            LikeTrackKey = LikeTrackKey,
            LikeTrackModifiers = LikeTrackModifiers,
            ShowOverlayOnMediaKeys = ShowOverlayOnMediaKeys
        };
    }

    public void ApplyVolumeDown(ShortcutBinding binding)
    {
        VolumeDownKey = binding.VirtualKeyCode;
        VolumeDownModifiers = binding.Modifiers;
    }

    public void ApplyVolumeUp(ShortcutBinding binding)
    {
        VolumeUpKey = binding.VirtualKeyCode;
        VolumeUpModifiers = binding.Modifiers;
    }

    public void ApplyMute(ShortcutBinding binding)
    {
        MuteKey = binding.VirtualKeyCode;
        MuteModifiers = binding.Modifiers;
    }

    public void ApplyPeekMedia(ShortcutBinding binding)
    {
        PeekMediaKey = binding.VirtualKeyCode;
        PeekMediaModifiers = binding.Modifiers;
    }

    public void ApplyPreviousTrack(ShortcutBinding binding)
    {
        PreviousTrackKey = binding.VirtualKeyCode;
        PreviousTrackModifiers = binding.Modifiers;
    }

    public void ApplyNextTrack(ShortcutBinding binding)
    {
        NextTrackKey = binding.VirtualKeyCode;
        NextTrackModifiers = binding.Modifiers;
    }

    public void ApplyPlayPause(ShortcutBinding binding)
    {
        PlayPauseKey = binding.VirtualKeyCode;
        PlayPauseModifiers = binding.Modifiers;
    }

    public void ApplyStop(ShortcutBinding binding)
    {
        StopKey = binding.VirtualKeyCode;
        StopModifiers = binding.Modifiers;
    }

    public void ApplyLikeTrack(ShortcutBinding binding)
    {
        LikeTrackKey = binding.VirtualKeyCode;
        LikeTrackModifiers = binding.Modifiers;
    }
}

internal sealed class ProfileSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Padrao";

    public string? LastDeviceId { get; set; }

    public string? LastDeviceName { get; set; }

    public bool FollowDefaultDevice { get; set; }

    public TargetMode TargetMode { get; set; } = TargetMode.Session;

    public string? LastSessionIdentifier { get; set; }

    public string? LastProcessName { get; set; }

    public int? LastProcessId { get; set; }

    public int StepPercent { get; set; } = 5;

    public ShortcutSettings Shortcuts { get; set; } = new();

    public static ProfileSettings FromCurrent(AppSettings settings, string name)
    {
        return new ProfileSettings
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(name) ? "Padrao" : name.Trim(),
            LastDeviceId = settings.LastDeviceId,
            LastDeviceName = settings.LastDeviceName,
            FollowDefaultDevice = settings.FollowDefaultDevice,
            TargetMode = settings.TargetMode,
            LastSessionIdentifier = settings.LastSessionIdentifier,
            LastProcessName = settings.LastProcessName,
            LastProcessId = settings.LastProcessId,
            StepPercent = settings.StepPercent,
            Shortcuts = settings.Shortcuts.Clone()
        };
    }

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            Id = Guid.NewGuid().ToString("N");
        }

        Name = string.IsNullOrWhiteSpace(Name) ? "Padrao" : Name.Trim();
        StepPercent = Math.Clamp(StepPercent, 1, 50);
        if (!Enum.IsDefined(TargetMode))
        {
            TargetMode = TargetMode.Session;
        }

        Shortcuts ??= new ShortcutSettings();
        Shortcuts.Normalize();
    }

    public override string ToString()
    {
        return Name;
    }
}

internal static class AppIconLoader
{
    public static Icon Load()
    {
        foreach (var path in CandidatePaths())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using var bitmap = new Bitmap(path);
                var handle = bitmap.GetHicon();
                try
                {
                    using var icon = Icon.FromHandle(handle);
                    return (Icon)icon.Clone();
                }
                finally
                {
                    NativeMethods.DestroyIcon(handle);
                }
            }
            catch
            {
                // Fall back to the system icon if the PNG is missing or malformed.
            }
        }

        try
        {
            using var stream = typeof(AppIconLoader).Assembly.GetManifestResourceStream("ico.png");
            if (stream is not null)
            {
                using var bitmap = new Bitmap(stream);
                var handle = bitmap.GetHicon();
                try
                {
                    using var icon = Icon.FromHandle(handle);
                    return (Icon)icon.Clone();
                }
                finally
                {
                    NativeMethods.DestroyIcon(handle);
                }
            }
        }
        catch
        {
            // The embedded icon is a fallback only.
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    private static IEnumerable<string> CandidatePaths()
    {
        var fileName = "ico.png";
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var directory = Path.GetDirectoryName(processPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                yield return Path.Combine(directory, fileName);
            }
        }

        yield return Path.Combine(AppContext.BaseDirectory, fileName);
        yield return Path.Combine(Directory.GetCurrentDirectory(), fileName);
    }
}

internal static class StartupManager
{
    private const string AppName = "VolumeKeyRouter";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        if (key?.GetValue(AppName) is string value &&
            value.Contains(GetExecutablePath(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public static void SetEnabled(bool enabled, bool startMinimized = false)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (enabled)
        {
            var arguments = startMinimized ? "--ui --start-minimized" : "--ui";
            key.SetValue(AppName, $"\"{GetExecutablePath()}\" {arguments}", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }

    private static string GetExecutablePath()
    {
        return Environment.ProcessPath ?? Application.ExecutablePath;
    }
}
