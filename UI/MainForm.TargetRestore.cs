using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VolumeKeyRouter;

internal sealed partial class MainForm
{
    private void RefreshDevices(bool restoreSavedTarget = false)
    {
        refreshing = true;
        try
        {
            var previousId = restoreSavedTarget ? settings.LastDeviceId : SelectedDevice?.Id ?? settings.LastDeviceId;
            var hasSavedDevice = !string.IsNullOrWhiteSpace(settings.LastDeviceId) ||
                !string.IsNullOrWhiteSpace(settings.LastDeviceName);
            var devices = audioManager.ListOutputDevices();

            deviceCombo.BeginUpdate();
            deviceCombo.Items.Clear();
            foreach (var device in devices)
            {
                deviceCombo.Items.Add(device);
            }

            var selected = devices.FirstOrDefault(device => device.Id == previousId)
                ?? devices.FirstOrDefault(device =>
                    !string.IsNullOrWhiteSpace(settings.LastDeviceName) &&
                    device.Name.Contains(settings.LastDeviceName, StringComparison.OrdinalIgnoreCase));

            if (!restoreSavedTarget || !hasSavedDevice)
            {
                selected ??= devices.FirstOrDefault(device => device.IsDefault)
                    ?? devices.FirstOrDefault();
            }

            if (selected is not null)
            {
                deviceCombo.SelectedItem = selected;
            }
            deviceCombo.EndUpdate();
        }
        catch (Exception ex)
        {
            SetStatus($"Erro ao listar dispositivos: {ex.Message}");
        }
        finally
        {
            refreshing = false;
        }

        RefreshSessions(restoreSavedTarget);
    }

    private void RefreshSessions(bool restoreSavedTarget = false)
    {
        var device = SelectedDevice;
        sessionList.BeginUpdate();
        try
        {
            var previousSessionId = restoreSavedTarget
                ? settings.LastSessionIdentifier
                : SelectedSession?.SessionIdentifier ?? settings.LastSessionIdentifier;
            var previousProcessName = restoreSavedTarget
                ? settings.LastProcessName
                : settings.LastProcessName;
            var previousProcessId = restoreSavedTarget ? settings.LastProcessId : null;
            sessionList.Items.Clear();

            if (device is null)
            {
                SetStatus("Nenhum dispositivo de saida encontrado.");
                return;
            }

            var sessions = audioManager.ListSessions(device.Id);
            foreach (var session in sessions)
            {
                sessionList.Items.Add(CreateSessionItem(session));
            }

            var itemToSelect = sessionList.Items
                .Cast<ListViewItem>()
                .FirstOrDefault(item => item.Tag is AudioSessionInfo session && session.SessionIdentifier == previousSessionId)
                ?? sessionList.Items
                    .Cast<ListViewItem>()
                    .FirstOrDefault(item =>
                        item.Tag is AudioSessionInfo session &&
                        !string.IsNullOrWhiteSpace(previousProcessName) &&
                        session.MatchesText(previousProcessName));

            if (itemToSelect is null && previousProcessId.HasValue)
            {
                itemToSelect = sessionList.Items
                    .Cast<ListViewItem>()
                    .FirstOrDefault(item =>
                        item.Tag is AudioSessionInfo session &&
                        session.ProcessId == previousProcessId.Value);
            }

            if (!restoreSavedTarget)
            {
                itemToSelect ??= sessionList.Items
                    .Cast<ListViewItem>()
                    .FirstOrDefault(item => item.Tag is AudioSessionInfo session && session.MatchesText("spotify"))
                    ?? sessionList.Items.Cast<ListViewItem>().FirstOrDefault();
            }

            if (itemToSelect is not null)
            {
                itemToSelect.Selected = true;
                itemToSelect.Focused = true;
            }

            SetStatus($"{sessions.Count} sessao/s em {device.Name}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Erro ao listar sessoes: {ex.Message}");
        }
        finally
        {
            sessionList.EndUpdate();
            UpdateTargetSnapshot();
        }
    }

    private bool HasSavedTarget()
    {
        if (!string.IsNullOrWhiteSpace(settings.LastDeviceId) ||
            !string.IsNullOrWhiteSpace(settings.LastDeviceName))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(settings.LastSessionIdentifier) ||
            !string.IsNullOrWhiteSpace(settings.LastProcessName) ||
            settings.LastProcessId.HasValue;
    }

    private void TryRestoreSavedTarget()
    {
        if (!restoringSavedTarget)
        {
            return;
        }

        var restored = false;
        var status = string.Empty;

        suppressSettingsSave = true;
        suppressSavedTargetCancel = true;
        try
        {
            var devices = audioManager.ListOutputDevices();
            var device = FindSavedDevice(devices);
            if (device is null)
            {
                var name = string.IsNullOrWhiteSpace(settings.LastDeviceName)
                    ? "dispositivo salvo"
                    : settings.LastDeviceName;
                status = $"Aguardando {name} aparecer...";
                return;
            }

            if (settings.TargetMode == TargetMode.Device)
            {
                SelectDeviceSilently(device);
                deviceModeButton.Checked = true;
                restored = true;
                status = $"Linha restaurada: {device.Name}.";
                return;
            }

            var sessions = audioManager.ListSessions(device.Id);
            var session = FindSavedSession(sessions);
            if (session is not null)
            {
                SelectDeviceSilently(device);
                appModeButton.Checked = true;
                SelectSessionSilently(session);
                restored = true;
                status = $"App restaurado: {session.ProcessName}.";
                return;
            }

            var targetName = string.IsNullOrWhiteSpace(settings.LastProcessName)
                ? "app salvo"
                : settings.LastProcessName;
            status = $"Aguardando {targetName} aparecer em {device.Name}...";
        }
        finally
        {
            suppressSavedTargetCancel = false;
            suppressSettingsSave = false;

            if (restored)
            {
                restoringSavedTarget = false;
                savedTargetSearchTimer.Stop();
                UpdateTargetSnapshot();
            }
            else
            {
                savedTargetSearchTimer.Start();
            }

            if (!string.IsNullOrWhiteSpace(status) && statusLabel.Text != status)
            {
                SetStatus(status);
            }
        }
    }

    private AudioDeviceInfo? FindSavedDevice(IReadOnlyList<AudioDeviceInfo> devices)
    {
        var hasSavedDevice = !string.IsNullOrWhiteSpace(settings.LastDeviceId) ||
            !string.IsNullOrWhiteSpace(settings.LastDeviceName);

        if (!hasSavedDevice)
        {
            return SelectedDevice ??
                devices.FirstOrDefault(device => device.IsDefault) ??
                devices.FirstOrDefault();
        }

        return devices.FirstOrDefault(device => device.Id == settings.LastDeviceId)
            ?? devices.FirstOrDefault(device =>
                !string.IsNullOrWhiteSpace(settings.LastDeviceName) &&
                device.Name.Contains(settings.LastDeviceName, StringComparison.OrdinalIgnoreCase));
    }

    private AudioSessionInfo? FindSavedSession(IReadOnlyList<AudioSessionInfo> sessions)
    {
        return sessions.FirstOrDefault(session =>
                !string.IsNullOrWhiteSpace(settings.LastSessionIdentifier) &&
                session.SessionIdentifier == settings.LastSessionIdentifier)
            ?? sessions.FirstOrDefault(session =>
                !string.IsNullOrWhiteSpace(settings.LastProcessName) &&
                session.MatchesText(settings.LastProcessName))
            ?? sessions.FirstOrDefault(session =>
                settings.LastProcessId.HasValue &&
                session.ProcessId == settings.LastProcessId.Value);
    }

    private bool SessionMatchesSavedTarget(AudioSessionInfo session)
    {
        if (!string.IsNullOrWhiteSpace(settings.LastSessionIdentifier) &&
            session.SessionIdentifier == settings.LastSessionIdentifier)
        {
            return true;
        }

        if (settings.LastProcessId.HasValue && session.ProcessId == settings.LastProcessId.Value)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(settings.LastProcessName) &&
            session.MatchesText(settings.LastProcessName);
    }

    private void SelectDeviceSilently(AudioDeviceInfo device)
    {
        var previousRefreshing = refreshing;
        refreshing = true;
        try
        {
            var item = deviceCombo.Items
                .Cast<AudioDeviceInfo>()
                .FirstOrDefault(candidate => candidate.Id == device.Id);

            if (item is null)
            {
                deviceCombo.Items.Add(device);
                item = device;
            }

            deviceCombo.SelectedItem = item;
        }
        finally
        {
            refreshing = previousRefreshing;
        }
    }

    private void SelectSessionSilently(AudioSessionInfo session)
    {
        var item = sessionList.Items
            .Cast<ListViewItem>()
            .FirstOrDefault(candidate =>
                candidate.Tag is AudioSessionInfo existing &&
                SessionsReferToSameTarget(existing, session));

        if (item is null)
        {
            item = CreateSessionItem(session);
            sessionList.Items.Add(item);
        }
        else
        {
            UpdateSessionItem(item, session);
        }

        item.Selected = true;
        item.Focused = true;
        item.EnsureVisible();
    }

    private static bool SessionsReferToSameTarget(AudioSessionInfo left, AudioSessionInfo right)
    {
        if (!string.IsNullOrWhiteSpace(left.SessionIdentifier) &&
            left.SessionIdentifier == right.SessionIdentifier)
        {
            return true;
        }

        if (left.ProcessId != 0 && left.ProcessId == right.ProcessId)
        {
            return true;
        }

        return left.ProcessName.Equals(right.ProcessName, StringComparison.OrdinalIgnoreCase);
    }

    private static ListViewItem CreateSessionItem(AudioSessionInfo session)
    {
        var item = new ListViewItem(session.ProcessName);
        item.SubItems.Add(session.ProcessId == 0 ? "-" : session.ProcessId.ToString(CultureInfo.InvariantCulture));
        item.SubItems.Add(session.State.ToString());
        item.SubItems.Add(session.Volume.ToString("P0", CultureInfo.CurrentCulture));
        item.SubItems.Add(session.DisplayName ?? session.ShortSessionIdentifier);
        item.Tag = session;
        return item;
    }

    private static void UpdateSessionItem(ListViewItem item, AudioSessionInfo session)
    {
        item.Text = session.ProcessName;
        item.SubItems[1].Text = session.ProcessId == 0 ? "-" : session.ProcessId.ToString(CultureInfo.InvariantCulture);
        item.SubItems[2].Text = session.State.ToString();
        item.SubItems[3].Text = session.Volume.ToString("P0", CultureInfo.CurrentCulture);
        item.SubItems[4].Text = session.DisplayName ?? session.ShortSessionIdentifier;
        item.Tag = session;
    }

    private void CancelSavedTargetSearchFromUser()
    {
        if (!restoringSavedTarget || suppressSavedTargetCancel || suppressSettingsSave)
        {
            return;
        }

        restoringSavedTarget = false;
        savedTargetSearchTimer.Stop();
        SetStatus("Busca do alvo salvo cancelada.");
    }

    private AudioDeviceInfo? SelectedDevice => deviceCombo.SelectedItem as AudioDeviceInfo;

    private AudioSessionInfo? SelectedSession =>
        sessionList.SelectedItems.Count == 0 ? null : sessionList.SelectedItems[0].Tag as AudioSessionInfo;
}
