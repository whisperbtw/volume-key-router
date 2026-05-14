using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
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

    public static AppSettings Load()
    {
        try
        {
            var path = File.Exists(SettingsPath)
                ? SettingsPath
                : LegacySettingsPath;

            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions)
                ?? new AppSettings();
            settings.StepPercent = Math.Clamp(settings.StepPercent, 1, 50);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "volume-key-router",
        "settings.json");

    private static string LegacySettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "spotify-audio-keys",
        "settings.json");
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
    private static readonly string[] LegacyAppNames = ["VolumeKeyRouter"];
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        if (key?.GetValue(AppName) is string value &&
            value.Contains(GetExecutablePath(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return LegacyAppNames.Any(name => key?.GetValue(name) is string);
    }

    public static void SetEnabled(bool enabled, bool startMinimized = false)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (enabled)
        {
            RemoveLegacyValues(key);
            var arguments = startMinimized ? "--ui --start-minimized" : "--ui";
            key.SetValue(AppName, $"\"{GetExecutablePath()}\" {arguments}", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
            RemoveLegacyValues(key);
        }
    }

    private static void RemoveLegacyValues(RegistryKey key)
    {
        foreach (var legacyName in LegacyAppNames)
        {
            key.DeleteValue(legacyName, throwOnMissingValue: false);
        }
    }

    private static string GetExecutablePath()
    {
        return Environment.ProcessPath ?? Application.ExecutablePath;
    }
}
