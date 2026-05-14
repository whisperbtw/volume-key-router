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
    private void InitializeRuntime()
    {
        if (runtimeInitialized)
        {
            return;
        }

        runtimeInitialized = true;

        var shouldStartCapture = settings.CaptureActive;
        StartActivationWatcher();

        suppressSettingsSave = true;
        try
        {
            ApplySettingsToControls();
            restoringSavedTarget = HasSavedTarget();
            suppressSavedTargetCancel = true;
            RefreshDevices(restoringSavedTarget);
        }
        finally
        {
            suppressSavedTargetCancel = false;
            suppressSettingsSave = false;
        }

        if (restoringSavedTarget)
        {
            TryRestoreSavedTarget();
        }

        if (shouldStartCapture)
        {
            StartCapture(persist: false);
        }
        else
        {
            captureActive = false;
            UpdateCaptureButton();
            SetStatus("Captura pausada. Ative quando quiser interceptar Fn+F2/F3.");
        }

        settings.CaptureActive = shouldStartCapture && captureActive;
        SaveSettings();
    }

    private void StartActivationWatcher()
    {
        if (activationEvent is null)
        {
            return;
        }

        Task.Run(() =>
        {
            var handles = new WaitHandle[]
            {
                activationEvent,
                activationWatcherCancellation.Token.WaitHandle
            };

            while (!activationWatcherCancellation.IsCancellationRequested)
            {
                var signaled = WaitHandle.WaitAny(handles);
                if (signaled != 0)
                {
                    break;
                }

                BeginInvokeSafe(RestoreFromTray);
            }
        });
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.UseImmersiveDarkMode(Handle);
    }

    protected override void SetVisibleCore(bool value)
    {
        if (value && startHiddenToTray)
        {
            InitializeRuntime();
            trayIcon.Visible = true;
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            UpdateTrayMenu();
            base.SetVisibleCore(false);
            return;
        }

        base.SetVisibleCore(value);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WmShowExistingApp)
        {
            RestoreFromTray();
            return;
        }

        base.WndProc(ref m);
    }

    private void ApplySettingsToControls()
    {
        var previousSuppressSettingsSave = suppressSettingsSave;
        suppressSettingsSave = true;
        try
        {
            appModeButton.Checked = settings.TargetMode != TargetMode.Device;
            deviceModeButton.Checked = settings.TargetMode == TargetMode.Device;
            stepPicker.Value = Math.Clamp(settings.StepPercent, (int)stepPicker.Minimum, (int)stepPicker.Maximum);
            UpdateStepValueLabel();
            var startupEnabled = StartupManager.IsEnabled();
            startWithWindowsBox.Checked = startupEnabled;
            if (startupEnabled)
            {
                StartupManager.SetEnabled(true, settings.StartMinimized);
            }
            minimizeToTrayBox.Checked = settings.MinimizeToTray;
            startMinimizedBox.Checked = settings.StartMinimized;
            showOverlayBox.Checked = settings.ShowVolumeOverlay;
        }
        finally
        {
            suppressSettingsSave = previousSuppressSettingsSave;
        }
    }

    private void SetStatus(string message)
    {
        statusLabel.Text = message;
        trayIcon.Text = message.Length > 63 ? message[..60] + "..." : message;
    }

    private void SaveSettings()
    {
        if (suppressSettingsSave)
        {
            return;
        }

        settings.CaptureActive = captureActive;
        settings.MinimizeToTray = minimizeToTrayBox.Checked;
        settings.StartMinimized = startMinimizedBox.Checked;
        settings.StartWithWindows = startWithWindowsBox.Checked;
        settings.ShowVolumeOverlay = showOverlayBox.Checked;
        settings.Save();
    }

    private void BeginInvokeSafe(Action action)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // The form is closing.
        }
    }
}
