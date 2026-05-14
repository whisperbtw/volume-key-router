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
    private void ToggleCapture()
    {
        if (captureActive)
        {
            StopCapture();
        }
        else
        {
            StartCapture();
        }
    }

    private void StartCapture(bool persist = true)
    {
        if (captureActive)
        {
            return;
        }

        try
        {
            hook = new VolumeKeyHook(HandleVolumeKey, HasValidTarget);
            hook.Install();
            captureActive = true;
            UpdateCaptureButton();
            if (persist)
            {
                settings.CaptureActive = true;
                SaveSettings();
            }

            SetStatus("Captura ativa. Fn+F2/F3 vai controlar o alvo selecionado.");
        }
        catch (Exception ex)
        {
            hook?.Dispose();
            hook = null;
            captureActive = false;
            UpdateCaptureButton();
            if (persist)
            {
                settings.CaptureActive = false;
                SaveSettings();
            }

            SetStatus($"Nao foi possivel ativar a captura: {ex.Message}");
        }
    }

    private void StopCapture(bool persist = true)
    {
        hook?.Dispose();
        hook = null;
        captureActive = false;
        UpdateCaptureButton();
        if (persist)
        {
            settings.CaptureActive = false;
            SaveSettings();
        }

        SetStatus("Captura pausada. As teclas de volume voltaram ao Windows.");
    }

    private void UpdateCaptureButton()
    {
        captureButton.Text = captureActive ? "Pausar captura" : "Ativar captura";
        captureButton.BackColor = captureActive ? Color.FromArgb(24, 95, 58) : DarkControl;
        captureButton.FlatAppearance.BorderColor = captureActive ? Accent : DarkBorder;
        UpdateTrayMenu();
    }

    private void UpdateStepValueLabel()
    {
        stepValueLabel.Text = "%";
    }

    private bool HandleVolumeKey(VolumeCommand command)
    {
        var snapshot = GetTargetSnapshot();
        if (!snapshot.IsValid)
        {
            BeginInvokeSafe(() => SetStatus("Escolha um app ou uma linha/dispositivo antes de usar as teclas."));
            return false;
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                VolumeAdjustmentResult result;
                lock (applyGate)
                {
                    result = snapshot.Mode == TargetMode.Device
                        ? audioManager.ApplyToEndpoint(snapshot.DeviceId, snapshot.Step, 0, 1, command)
                        : audioManager.ApplyToSessions(
                            snapshot.DeviceId,
                            snapshot.SessionTarget,
                            snapshot.Step,
                            0,
                            1,
                            command);
                }

                BeginInvokeSafe(() =>
                {
                    if (result.ChangedSessions == 0)
                    {
                        SetStatus($"Nao encontrei o alvo atual. Clique em Atualizar e selecione de novo.");
                    }
                    else
                    {
                        SetStatus($"{result.TargetLabel}: {result.Before:P0} -> {result.After:P0}");
                        UpdateSelectedSessionVolume(result.After);
                        if (showOverlayBox.Checked)
                        {
                            volumeOverlay.ShowVolume(result.TargetLabel, result.After);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                BeginInvokeSafe(() => SetStatus($"Erro ao ajustar volume: {ex.Message}"));
            }
        });

        return true;
    }

    private void UpdateSelectedSessionVolume(float volume)
    {
        if (targetSnapshot.Mode != TargetMode.Session || sessionList.SelectedItems.Count == 0)
        {
            return;
        }

        sessionList.SelectedItems[0].SubItems[3].Text = volume.ToString("P0", CultureInfo.CurrentCulture);
        sessionList.Invalidate(sessionList.SelectedItems[0].Bounds);
    }

    private bool HasValidTarget()
    {
        return GetTargetSnapshot().IsValid;
    }

    private TargetSnapshot GetTargetSnapshot()
    {
        lock (targetGate)
        {
            return targetSnapshot;
        }
    }

    private void UpdateTargetSnapshot()
    {
        var device = SelectedDevice;
        var selectedSession = SelectedSession;
        var step = (float)stepPicker.Value / 100f;

        TargetSnapshot snapshot;
        if (device is null)
        {
            snapshot = TargetSnapshot.Invalid;
        }
        else if (deviceModeButton.Checked)
        {
            snapshot = new TargetSnapshot(
                true,
                TargetMode.Device,
                device.Id,
                new SessionTarget(null, null, null, Array.Empty<string>(), new HashSet<int>()),
                step,
                device.Name);
        }
        else if (selectedSession is not null)
        {
            snapshot = new TargetSnapshot(
                true,
                TargetMode.Session,
                device.Id,
                new SessionTarget(
                    selectedSession.SessionIdentifier,
                    selectedSession.ProcessId == 0 ? null : (int)selectedSession.ProcessId,
                    selectedSession.ProcessName,
                    Array.Empty<string>(),
                    new HashSet<int>()),
                step,
                selectedSession.ProcessName);
        }
        else
        {
            snapshot = TargetSnapshot.Invalid;
        }

        lock (targetGate)
        {
            targetSnapshot = snapshot;
        }

        targetLabel.Text = snapshot.IsValid
            ? $"Alvo: {snapshot.DisplayName} | Passo: {snapshot.Step:P0}"
            : "Alvo: nenhum";

        if (!suppressSettingsSave)
        {
            settings.StepPercent = (int)stepPicker.Value;
            settings.TargetMode = deviceModeButton.Checked ? TargetMode.Device : TargetMode.Session;

            if (device is not null)
            {
                settings.LastDeviceId = device.Id;
                settings.LastDeviceName = device.Name;
            }

            if (selectedSession is not null)
            {
                settings.LastSessionIdentifier = selectedSession.SessionIdentifier;
                settings.LastProcessName = selectedSession.ProcessName;
                settings.LastProcessId = selectedSession.ProcessId == 0 ? null : (int)selectedSession.ProcessId;
            }

            SaveSettings();
        }
    }
}
