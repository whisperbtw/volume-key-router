using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VolumeKeyRouter;

[SupportedOSPlatform("windows")]
internal sealed class AudioManager : IDisposable
{
    private readonly NAudio.CoreAudioApi.MMDeviceEnumerator deviceEnumerator = new();
    private readonly object gate = new();

    public IReadOnlyList<AudioDeviceInfo> ListOutputDevices()
    {
        lock (gate)
        {
            var defaultDeviceId = string.Empty;
            var devices = new List<AudioDeviceInfo>();

            if (deviceEnumerator.HasDefaultAudioEndpoint(
                NAudio.CoreAudioApi.DataFlow.Render,
                NAudio.CoreAudioApi.Role.Multimedia))
            {
                using var defaultDevice = deviceEnumerator.GetDefaultAudioEndpoint(
                    NAudio.CoreAudioApi.DataFlow.Render,
                    NAudio.CoreAudioApi.Role.Multimedia);
                defaultDeviceId = defaultDevice.ID;
            }

            var endpoints = deviceEnumerator.EnumerateAudioEndPoints(
                NAudio.CoreAudioApi.DataFlow.Render,
                NAudio.CoreAudioApi.DeviceState.Active);

            foreach (var device in endpoints)
            {
                try
                {
                    devices.Add(new AudioDeviceInfo(
                        device.ID,
                        GetReadableDeviceName(device),
                        string.Equals(device.ID, defaultDeviceId, StringComparison.Ordinal)));
                }
                finally
                {
                    if (device is IDisposable disposableDevice)
                    {
                        disposableDevice.Dispose();
                    }
                }
            }

            return devices
                .OrderByDescending(device => device.IsDefault)
                .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
    }

    public IReadOnlyList<AudioSessionInfo> ListSessions(string deviceId)
    {
        lock (gate)
        {
            var result = new List<AudioSessionInfo>();

            using var device = OpenDevice(deviceId);
            var manager = device.AudioSessionManager;
            manager.RefreshSessions();
            var sessions = manager.Sessions;

            for (var index = 0; index < sessions.Count; index++)
            {
                var session = sessions[index];
                var processId = session.GetProcessID;
                var processName = TryGetProcessName(processId) ?? "(desconhecido)";
                var displayName = NullIfWhiteSpace(session.DisplayName);
                var sessionIdentifier = NullIfWhiteSpace(session.GetSessionIdentifier);

                result.Add(new AudioSessionInfo(
                    processId,
                    processName,
                    NormalizeSessionState(session.State.ToString()),
                    session.SimpleAudioVolume.Volume,
                    displayName,
                    sessionIdentifier));
            }

            return result
                .OrderByDescending(session => session.State.Equals("Active", StringComparison.OrdinalIgnoreCase))
                .ThenBy(session => session.ProcessName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
    }

    public VolumeAdjustmentResult ApplyToSessions(
        string deviceId,
        SessionTarget target,
        float step,
        float minimum,
        float maximum,
        VolumeCommand command)
    {
        lock (gate)
        {
            var changed = 0;
            var beforeTotal = 0.0f;
            var afterTotal = 0.0f;
            var labels = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            using var device = OpenDevice(deviceId);
            var manager = device.AudioSessionManager;
            manager.RefreshSessions();
            var sessions = manager.Sessions;

            for (var index = 0; index < sessions.Count; index++)
            {
                var session = sessions[index];
                var processId = session.GetProcessID;
                if (!MatchesTarget(session, processId, target, out var processLabel))
                {
                    continue;
                }

                var volumeControl = session.SimpleAudioVolume;
                var currentVolume = volumeControl.Volume;
                var nextVolume = currentVolume;
                var isMuted = false;
                if (command == VolumeCommand.Mute)
                {
                    isMuted = !volumeControl.Mute;
                    volumeControl.Mute = isMuted;
                }
                else
                {
                    if (volumeControl.Mute)
                    {
                        volumeControl.Mute = false;
                    }

                    nextVolume = CalculateNextVolume(currentVolume, step, minimum, maximum, command);
                    volumeControl.Volume = nextVolume;
                }

                changed++;
                beforeTotal += currentVolume;
                afterTotal += nextVolume;
                labels.Add(processLabel);
            }

            return changed == 0
                ? VolumeAdjustmentResult.NotFound
                : new VolumeAdjustmentResult(
                    changed,
                    beforeTotal / changed,
                    afterTotal / changed,
                    string.Join(", ", labels),
                    command == VolumeCommand.Mute,
                    command == VolumeCommand.Mute && afterTotal >= 0 && IsTargetMuted(deviceId, target));
        }
    }

    public VolumeAdjustmentResult ApplyToEndpoint(
        string deviceId,
        float step,
        float minimum,
        float maximum,
        VolumeCommand command)
    {
        lock (gate)
        {
            using var device = OpenDevice(deviceId);
            var deviceName = GetReadableDeviceName(device);
            var endpointVolume = device.AudioEndpointVolume;
            var currentVolume = endpointVolume.MasterVolumeLevelScalar;
            var nextVolume = currentVolume;
            var isMuted = endpointVolume.Mute;
            if (command == VolumeCommand.Mute)
            {
                isMuted = !isMuted;
                endpointVolume.Mute = isMuted;
            }
            else
            {
                if (isMuted)
                {
                    endpointVolume.Mute = false;
                    isMuted = false;
                }

                nextVolume = CalculateNextVolume(currentVolume, step, minimum, maximum, command);
                endpointVolume.MasterVolumeLevelScalar = nextVolume;
            }

            return new VolumeAdjustmentResult(
                1,
                currentVolume,
                nextVolume,
                deviceName,
                command == VolumeCommand.Mute,
                isMuted);
        }
    }

    public TargetVolumeState GetSessionVolumeState(string deviceId, SessionTarget target)
    {
        lock (gate)
        {
            var matched = 0;
            var muted = 0;
            var volumeTotal = 0.0f;
            var labels = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            using var device = OpenDevice(deviceId);
            var manager = device.AudioSessionManager;
            manager.RefreshSessions();
            var sessions = manager.Sessions;

            for (var index = 0; index < sessions.Count; index++)
            {
                var session = sessions[index];
                var processId = session.GetProcessID;
                if (!MatchesTarget(session, processId, target, out var processLabel))
                {
                    continue;
                }

                var volumeControl = session.SimpleAudioVolume;
                matched++;
                volumeTotal += volumeControl.Volume;
                if (volumeControl.Mute)
                {
                    muted++;
                }

                labels.Add(processLabel);
            }

            return matched == 0
                ? TargetVolumeState.NotFound
                : new TargetVolumeState(
                    true,
                    volumeTotal / matched,
                    muted == matched,
                    string.Join(", ", labels));
        }
    }

    public TargetVolumeState GetEndpointVolumeState(string deviceId)
    {
        lock (gate)
        {
            using var device = OpenDevice(deviceId);
            var endpointVolume = device.AudioEndpointVolume;
            return new TargetVolumeState(
                true,
                endpointVolume.MasterVolumeLevelScalar,
                endpointVolume.Mute,
                GetReadableDeviceName(device));
        }
    }

    public void Dispose()
    {
        deviceEnumerator.Dispose();
    }

    private NAudio.CoreAudioApi.MMDevice OpenDevice(string deviceId)
    {
        return deviceEnumerator.GetDevice(deviceId);
    }

    private bool IsTargetMuted(string deviceId, SessionTarget target)
    {
        using var device = OpenDevice(deviceId);
        var manager = device.AudioSessionManager;
        manager.RefreshSessions();
        var sessions = manager.Sessions;

        for (var index = 0; index < sessions.Count; index++)
        {
            var session = sessions[index];
            if (MatchesTarget(session, session.GetProcessID, target, out _))
            {
                return session.SimpleAudioVolume.Mute;
            }
        }

        return false;
    }

    private static float CalculateNextVolume(float currentVolume, float step, float minimum, float maximum, VolumeCommand command)
    {
        var nextVolume = command == VolumeCommand.Down
            ? currentVolume - step
            : currentVolume + step;
        return Math.Clamp(nextVolume, minimum, maximum);
    }

    private static bool MatchesTarget(
        NAudio.CoreAudioApi.AudioSessionControl session,
        uint processId,
        SessionTarget target,
        out string processLabel)
    {
        processLabel = $"PID {processId}";

        var sessionIdentifier = NullIfWhiteSpace(session.GetSessionIdentifier);
        if (!string.IsNullOrWhiteSpace(target.SessionIdentifier) &&
            string.Equals(sessionIdentifier, target.SessionIdentifier, StringComparison.Ordinal))
        {
            processLabel = TryGetProcessName(processId) ?? target.ProcessName ?? processLabel;
            return true;
        }

        if (processId > 0 && target.ProcessId == (int)processId)
        {
            processLabel = TryGetProcessName(processId) ?? target.ProcessName ?? processLabel;
            return true;
        }

        if (processId > 0 && target.ProcessIds.Contains((int)processId))
        {
            processLabel = TryGetProcessName(processId) ?? processLabel;
            return true;
        }

        var processName = TryGetProcessName(processId);
        if (processName is not null)
        {
            processLabel = processName;
            if (MatchesName(processName, target.ProcessName) ||
                target.ProcessNames.Contains(processName, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var displayName = NullIfWhiteSpace(session.DisplayName);
        if (MatchesAnyTarget(displayName, target))
        {
            processLabel = string.IsNullOrWhiteSpace(displayName) ? processLabel : displayName;
            return true;
        }

        if (MatchesAnyTarget(sessionIdentifier, target))
        {
            processLabel = processName ?? displayName ?? processLabel;
            return true;
        }

        return false;
    }

    private static bool MatchesAnyTarget(string? value, SessionTarget target)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (MatchesName(value, target.ProcessName))
        {
            return true;
        }

        return target.ProcessNames.Any(name => value.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesName(string? value, string? expected)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            !string.IsNullOrWhiteSpace(expected) &&
            value.Contains(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetProcessName(uint processId)
    {
        if (processId == 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string GetReadableDeviceName(NAudio.CoreAudioApi.MMDevice device)
    {
        if (!string.IsNullOrWhiteSpace(device.FriendlyName))
        {
            return device.FriendlyName;
        }

        if (!string.IsNullOrWhiteSpace(device.DeviceFriendlyName))
        {
            return device.DeviceFriendlyName;
        }

        return device.ID;
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string NormalizeSessionState(string state)
    {
        return state.StartsWith("AudioSessionState", StringComparison.Ordinal)
            ? state["AudioSessionState".Length..]
            : state;
    }
}
