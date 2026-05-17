using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VolumeKeyRouter;

internal enum TargetMode
{
    Session,
    Device
}

internal readonly record struct TargetSnapshot(
    bool IsValid,
    TargetMode Mode,
    string DeviceId,
    SessionTarget SessionTarget,
    float Step,
    string DisplayName)
{
    public static TargetSnapshot Invalid => new(
        false,
        TargetMode.Session,
        string.Empty,
        new SessionTarget(null, null, null, Array.Empty<string>(), new HashSet<int>()),
        0.05f,
        string.Empty);
}

internal readonly record struct SessionTarget(
    string? SessionIdentifier,
    int? ProcessId,
    string? ProcessName,
    IReadOnlyList<string> ProcessNames,
    IReadOnlySet<int> ProcessIds);

internal enum VolumeCommand
{
    Down,
    Up,
    Mute
}

internal enum MediaPlaybackState
{
    Unknown,
    Playing,
    Paused,
    Stopped
}

internal enum MediaKeyCommand
{
    Peek,
    PreviousTrack,
    NextTrack,
    PlayPause,
    Stop
}

internal sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault)
{
    public override string ToString()
    {
        return IsDefault ? $"{Name} (padrao)" : Name;
    }
}

internal sealed record AudioSessionInfo(
    uint ProcessId,
    string ProcessName,
    string State,
    float Volume,
    string? DisplayName,
    string? SessionIdentifier)
{
    public string ShortSessionIdentifier
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SessionIdentifier))
            {
                return string.Empty;
            }

            return SessionIdentifier.Length <= 80
                ? SessionIdentifier
                : SessionIdentifier[..80] + "...";
        }
    }

    public bool MatchesText(string value)
    {
        return ProcessName.Contains(value, StringComparison.OrdinalIgnoreCase) ||
            (DisplayName?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (SessionIdentifier?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}

internal readonly record struct VolumeAdjustmentResult(
    int ChangedSessions,
    float Before,
    float After,
    string TargetLabel,
    bool IsMuteCommand = false,
    bool IsMuted = false)
{
    public static VolumeAdjustmentResult NotFound => new(0, 0, 0, string.Empty);
}

internal readonly record struct TargetVolumeState(
    bool Found,
    float Volume,
    bool IsMuted,
    string TargetLabel)
{
    public static TargetVolumeState NotFound => new(false, 1, false, string.Empty);
}

internal sealed record MediaTrackInfo(
    string Title,
    string? Artist,
    byte[]? ArtworkBytes,
    MediaPlaybackState PlaybackState)
{
    public string DisplayText => string.IsNullOrWhiteSpace(Artist)
        ? Title
        : $"{Artist} - {Title}";

    public string OverlayTitle => PlaybackState switch
    {
        MediaPlaybackState.Paused => "Pausado",
        MediaPlaybackState.Stopped => "Parado",
        _ => "Tocando agora"
    };
}
