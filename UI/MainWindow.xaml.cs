using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace VolumeKeyRouter;

[SupportedOSPlatform("windows")]
public sealed partial class MainWindow : Window
{
    private readonly EventWaitHandle? activationEvent;
    private readonly EventWaitHandle? shutdownEvent;
    private readonly CancellationTokenSource activationWatcherCancellation = new();
    private readonly AppSettings settings;
    private readonly AudioManager audioManager = new();
    private readonly object targetGate = new();
    private readonly object applyGate = new();
    private readonly Forms.NotifyIcon trayIcon = new();
    private readonly Forms.ContextMenuStrip trayMenu = new();
    private readonly Forms.ToolStripMenuItem trayToggleCaptureItem = new();
    private readonly Forms.ToolStripMenuItem trayShowItem = new();
    private readonly Drawing.Icon appIcon;
    private readonly VolumeOverlayWindow volumeOverlay = new();
    private readonly DispatcherTimer savedTargetSearchTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(1500)
    };

    private VolumeKeyHook? hook;
    private TargetSnapshot targetSnapshot = TargetSnapshot.Invalid;
    private bool captureActive;
    private bool refreshing;
    private bool suppressSettingsSave = true;
    private bool restoringSavedTarget;
    private bool suppressSavedTargetCancel;
    private bool runtimeInitialized;
    private bool startHiddenToTray;
    private bool allowClose;

    public ObservableCollection<SessionRow> Sessions { get; } = new();

    public MainWindow(EventWaitHandle? activationEvent = null, EventWaitHandle? shutdownEvent = null, bool startMinimized = false)
    {
        this.activationEvent = activationEvent;
        this.shutdownEvent = shutdownEvent;
        settings = AppSettings.Load();
        startHiddenToTray = startMinimized;
        appIcon = AppIconLoader.Load();

        InitializeComponent();
        DataContext = this;
        Icon = Imaging.CreateBitmapSourceFromHIcon(appIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

        if (startHiddenToTray)
        {
            ShowInTaskbar = false;
            WindowState = WindowState.Minimized;
        }

        savedTargetSearchTimer.Tick += (_, _) => TryRestoreSavedTarget();
        Loaded += (_, _) =>
        {
            InitializeRuntime();
            if (startHiddenToTray)
            {
                HideToTray();
            }
        };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized && MinimizeToTrayBox.IsChecked == true)
            {
                HideToTray();
            }
        };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            NativeMethods.UseImmersiveDarkMode(handle);
            if (HwndSource.FromHwnd(handle) is { } source)
            {
                source.AddHook(WndProc);
            }
        };
        Closing += (_, eventArgs) =>
        {
            if (!allowClose && MinimizeToTrayBox.IsChecked == true)
            {
                eventArgs.Cancel = true;
                SaveSettings();
                HideToTray();
            }
        };
        Closed += (_, _) => Cleanup();

        BuildTray();
        UpdateCaptureButton();
    }

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
        if (activationEvent is null || shutdownEvent is null)
        {
            return;
        }

        Task.Run(() =>
        {
            var handles = new WaitHandle[]
            {
                activationEvent,
                shutdownEvent,
                activationWatcherCancellation.Token.WaitHandle
            };

            while (!activationWatcherCancellation.IsCancellationRequested)
            {
                var signaled = WaitHandle.WaitAny(handles);
                if (signaled == 0)
                {
                    BeginInvokeSafe(RestoreFromTray);
                    continue;
                }

                if (signaled == 1)
                {
                    BeginInvokeSafe(ExitApplication);
                    continue;
                }

                if (signaled != 0)
                {
                    break;
                }
            }
        });
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmShowExistingApp)
        {
            RestoreFromTray();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ApplySettingsToControls()
    {
        var previousSuppressSettingsSave = suppressSettingsSave;
        suppressSettingsSave = true;
        try
        {
            AppModeButton.IsChecked = settings.TargetMode != TargetMode.Device;
            DeviceModeButton.IsChecked = settings.TargetMode == TargetMode.Device;
            StepSlider.Value = Math.Clamp(settings.StepPercent, (int)StepSlider.Minimum, (int)StepSlider.Maximum);
            UpdateStepValueLabel();
            var startupEnabled = StartupManager.IsEnabled();
            StartWithWindowsBox.IsChecked = startupEnabled;
            if (startupEnabled)
            {
                StartupManager.SetEnabled(true, settings.StartMinimized);
            }
            MinimizeToTrayBox.IsChecked = settings.MinimizeToTray;
            StartMinimizedBox.IsChecked = settings.StartMinimized;
            ShowOverlayBox.IsChecked = settings.ShowVolumeOverlay;
        }
        finally
        {
            suppressSettingsSave = previousSuppressSettingsSave;
        }
    }

    private void RefreshDevices(bool restoreSavedTarget = false)
    {
        refreshing = true;
        try
        {
            var previousId = restoreSavedTarget ? settings.LastDeviceId : SelectedDevice?.Id ?? settings.LastDeviceId;
            var hasSavedDevice = !string.IsNullOrWhiteSpace(settings.LastDeviceId) ||
                !string.IsNullOrWhiteSpace(settings.LastDeviceName);
            var devices = audioManager.ListOutputDevices();

            DeviceCombo.ItemsSource = devices;
            var selected = devices.FirstOrDefault(device => device.Id == previousId)
                ?? devices.FirstOrDefault(device =>
                    !string.IsNullOrWhiteSpace(settings.LastDeviceName) &&
                    device.Name.Contains(settings.LastDeviceName, StringComparison.OrdinalIgnoreCase));

            if (!restoreSavedTarget || !hasSavedDevice)
            {
                selected ??= devices.FirstOrDefault(device => device.IsDefault) ?? devices.FirstOrDefault();
            }

            DeviceCombo.SelectedItem = selected;
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
        try
        {
            var previousSessionId = restoreSavedTarget
                ? settings.LastSessionIdentifier
                : SelectedSession?.SessionIdentifier ?? settings.LastSessionIdentifier;
            var previousProcessName = restoreSavedTarget
                ? settings.LastProcessName
                : settings.LastProcessName;
            var previousProcessId = restoreSavedTarget ? settings.LastProcessId : null;
            Sessions.Clear();

            if (device is null)
            {
                SetStatus("Nenhum dispositivo de saida encontrado.");
                return;
            }

            var sessions = audioManager.ListSessions(device.Id);
            foreach (var session in sessions)
            {
                Sessions.Add(new SessionRow(session));
            }

            var itemToSelect = Sessions.FirstOrDefault(row => row.Info.SessionIdentifier == previousSessionId)
                ?? Sessions.FirstOrDefault(row =>
                    !string.IsNullOrWhiteSpace(previousProcessName) &&
                    row.Info.MatchesText(previousProcessName));

            if (itemToSelect is null && previousProcessId.HasValue)
            {
                itemToSelect = Sessions.FirstOrDefault(row => row.Info.ProcessId == previousProcessId.Value);
            }

            if (!restoreSavedTarget)
            {
                itemToSelect ??= Sessions.FirstOrDefault(row => row.Info.MatchesText("spotify"))
                    ?? Sessions.FirstOrDefault();
            }

            SessionGrid.SelectedItem = itemToSelect;
            SetStatus($"{sessions.Count} sessao/s em {device.Name}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Erro ao listar sessoes: {ex.Message}");
        }
        finally
        {
            UpdateTargetSnapshot();
        }
    }

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
                        : audioManager.ApplyToSessions(snapshot.DeviceId, snapshot.SessionTarget, snapshot.Step, 0, 1, command);
                }

                BeginInvokeSafe(() =>
                {
                    if (result.ChangedSessions == 0)
                    {
                        SetStatus("Nao encontrei o alvo atual. Clique em Atualizar e selecione de novo.");
                    }
                    else
                    {
                        if (result.IsMuteCommand)
                        {
                            SetStatus($"{result.TargetLabel}: {(result.IsMuted ? "mutado" : "desmutado")}");
                        }
                        else
                        {
                            SetStatus($"{result.TargetLabel}: {result.Before:P0} -> {result.After:P0}");
                            UpdateSelectedSessionVolume(result.After);
                        }

                        if (ShowOverlayBox.IsChecked == true)
                        {
                            volumeOverlay.ShowVolume(result.TargetLabel, result.IsMuteCommand && result.IsMuted ? 0 : result.After);
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
        if (targetSnapshot.Mode != TargetMode.Session || SessionGrid.SelectedItem is not SessionRow row)
        {
            return;
        }

        row.SetVolume(volume);
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
        if (DeviceCombo is null ||
            SessionGrid is null ||
            StepSlider is null ||
            TargetText is null ||
            DeviceModeButton is null)
        {
            return;
        }

        var device = SelectedDevice;
        var selectedSession = SelectedSession;
        var step = (float)Math.Round(StepSlider.Value) / 100f;

        TargetSnapshot snapshot;
        if (device is null)
        {
            snapshot = TargetSnapshot.Invalid;
        }
        else if (DeviceModeButton.IsChecked == true)
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

        TargetText.Text = snapshot.IsValid
            ? $"Alvo: {snapshot.DisplayName} | Passo: {snapshot.Step:P0}"
            : "Alvo: nenhum";

        if (!suppressSettingsSave)
        {
            SaveSettings();
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
                DeviceModeButton.IsChecked = true;
                restored = true;
                status = $"Linha restaurada: {device.Name}.";
                return;
            }

            var sessions = audioManager.ListSessions(device.Id);
            var session = FindSavedSession(sessions);
            if (session is not null)
            {
                SelectDeviceSilently(device);
                AppModeButton.IsChecked = true;
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

            if (!string.IsNullOrWhiteSpace(status) && StatusText.Text != status)
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
            return SelectedDevice ?? devices.FirstOrDefault(device => device.IsDefault) ?? devices.FirstOrDefault();
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

    private void SelectDeviceSilently(AudioDeviceInfo device)
    {
        var previousRefreshing = refreshing;
        refreshing = true;
        try
        {
            var devices = DeviceCombo.ItemsSource as IEnumerable<AudioDeviceInfo> ?? Array.Empty<AudioDeviceInfo>();
            var item = devices.FirstOrDefault(candidate => candidate.Id == device.Id) ?? device;
            DeviceCombo.SelectedItem = item;
        }
        finally
        {
            refreshing = previousRefreshing;
        }
    }

    private void SelectSessionSilently(AudioSessionInfo session)
    {
        var row = Sessions.FirstOrDefault(candidate => SessionsReferToSameTarget(candidate.Info, session));
        if (row is null)
        {
            row = new SessionRow(session);
            Sessions.Add(row);
        }
        else
        {
            row.Update(session);
        }

        SessionGrid.SelectedItem = row;
        SessionGrid.ScrollIntoView(row);
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

    private void BuildTray()
    {
        trayShowItem.Text = "Abrir";
        trayShowItem.Click += (_, _) => BeginInvokeSafe(RestoreFromTray);

        trayToggleCaptureItem.Click += (_, _) => BeginInvokeSafe(ToggleCapture);

        var refreshItem = new Forms.ToolStripMenuItem("Atualizar", null, (_, _) => BeginInvokeSafe(() =>
        {
            RestoreFromTray();
            RefreshDevices();
        }));

        var exitItem = new Forms.ToolStripMenuItem("Sair", null, (_, _) => BeginInvokeSafe(ExitApplication));

        trayMenu.Items.Add(trayShowItem);
        trayMenu.Items.Add(trayToggleCaptureItem);
        trayMenu.Items.Add(refreshItem);
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.Items.Add(exitItem);

        trayIcon.Text = Program.AppDisplayName;
        trayIcon.Icon = appIcon;
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.Visible = true;
        trayIcon.MouseClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == Forms.MouseButtons.Left)
            {
                BeginInvokeSafe(RestoreFromTray);
            }
        };
        trayIcon.DoubleClick += (_, _) => BeginInvokeSafe(RestoreFromTray);

        UpdateTrayMenu();
    }

    private void HideToTray()
    {
        if (MinimizeToTrayBox.IsChecked != true && !startHiddenToTray)
        {
            return;
        }

        Hide();
        ShowInTaskbar = false;
        WindowState = WindowState.Minimized;
        trayIcon.Visible = true;
        UpdateTrayMenu();
    }

    private void RestoreFromTray()
    {
        startHiddenToTray = false;
        Show();
        ShowInTaskbar = true;
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
        UpdateTrayMenu();
    }

    private void ExitApplication()
    {
        allowClose = true;
        SaveSettings();
        Close();
    }

    private void UpdateCaptureButton()
    {
        CaptureButton.Content = captureActive ? "Pausar captura" : "Ativar captura";
        CaptureButton.Background = new SolidColorBrush(captureActive ? System.Windows.Media.Color.FromRgb(24, 95, 58) : System.Windows.Media.Color.FromRgb(31, 35, 44));
        CaptureButton.BorderBrush = new SolidColorBrush(captureActive ? System.Windows.Media.Color.FromRgb(38, 208, 124) : System.Windows.Media.Color.FromRgb(71, 79, 94));
        UpdateTrayMenu();
    }

    private void UpdateTrayMenu()
    {
        trayToggleCaptureItem.Text = captureActive ? "Pausar captura" : "Ativar captura";
        trayShowItem.Text = IsVisible && WindowState != WindowState.Minimized ? "Abrir" : "Mostrar janela";
    }

    private void UpdateStepValueLabel()
    {
        if (StepValueText is null || StepSlider is null)
        {
            return;
        }

        StepValueText.Text = $"{Math.Round(StepSlider.Value)}%";
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
        trayIcon.Text = message.Length > 63 ? message[..60] + "..." : message;
    }

    private void SaveSettings()
    {
        if (suppressSettingsSave)
        {
            return;
        }

        CopyCurrentControlsToSettings();
        settings.Save();
    }

    private void CopyCurrentControlsToSettings()
    {
        if (StepSlider is not null)
        {
            settings.StepPercent = Math.Clamp((int)Math.Round(StepSlider.Value), 1, 50);
        }

        if (DeviceModeButton is not null)
        {
            settings.TargetMode = DeviceModeButton.IsChecked == true ? TargetMode.Device : TargetMode.Session;
        }

        var device = SelectedDevice;
        if (device is not null)
        {
            settings.LastDeviceId = device.Id;
            settings.LastDeviceName = device.Name;
        }

        var selectedSession = SelectedSession;
        if (selectedSession is not null)
        {
            settings.LastSessionIdentifier = selectedSession.SessionIdentifier;
            settings.LastProcessName = selectedSession.ProcessName;
            settings.LastProcessId = selectedSession.ProcessId == 0 ? null : (int)selectedSession.ProcessId;
        }

        settings.CaptureActive = captureActive;
        settings.MinimizeToTray = MinimizeToTrayBox.IsChecked == true;
        settings.StartMinimized = StartMinimizedBox.IsChecked == true;
        settings.StartWithWindows = StartWithWindowsBox.IsChecked == true;
        settings.ShowVolumeOverlay = ShowOverlayBox.IsChecked == true;
    }

    private void BeginInvokeSafe(Action action)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        Dispatcher.BeginInvoke(action);
    }

    private void Cleanup()
    {
        SaveSettings();
        StopCapture(persist: false);
        activationWatcherCancellation.Cancel();
        activationEvent?.Set();
        activationWatcherCancellation.Dispose();
        savedTargetSearchTimer.Stop();
        trayIcon.Visible = false;
        trayIcon.Dispose();
        trayMenu.Dispose();
        volumeOverlay.Close();
        appIcon.Dispose();
        audioManager.Dispose();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshDevices();
    }

    private void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleCapture();
    }

    private void DeviceCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!refreshing)
        {
            CancelSavedTargetSearchFromUser();
            RefreshSessions();
        }
    }

    private void TargetMode_Checked(object sender, RoutedEventArgs e)
    {
        CancelSavedTargetSearchFromUser();
        UpdateTargetSnapshot();
    }

    private void StepSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateStepValueLabel();
        UpdateTargetSnapshot();
    }

    private void SessionGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        CancelSavedTargetSearchFromUser();
        UpdateTargetSnapshot();
    }

    private void StartWithWindowsBox_Changed(object sender, RoutedEventArgs e)
    {
        if (suppressSettingsSave)
        {
            return;
        }

        try
        {
            StartupManager.SetEnabled(StartWithWindowsBox.IsChecked == true, StartMinimizedBox.IsChecked == true);
            settings.StartWithWindows = StartWithWindowsBox.IsChecked == true;
            SaveSettings();
            SetStatus(StartWithWindowsBox.IsChecked == true
                ? "Inicializacao com Windows ativada."
                : "Inicializacao com Windows desativada.");
        }
        catch (Exception ex)
        {
            suppressSettingsSave = true;
            StartWithWindowsBox.IsChecked = StartupManager.IsEnabled();
            suppressSettingsSave = false;
            SetStatus($"Nao consegui alterar a inicializacao: {ex.Message}");
        }
    }

    private void MinimizeToTrayBox_Changed(object sender, RoutedEventArgs e)
    {
        if (suppressSettingsSave)
        {
            return;
        }

        settings.MinimizeToTray = MinimizeToTrayBox.IsChecked == true;
        SaveSettings();
    }

    private void StartMinimizedBox_Changed(object sender, RoutedEventArgs e)
    {
        if (suppressSettingsSave)
        {
            return;
        }

        settings.StartMinimized = StartMinimizedBox.IsChecked == true;
        SaveSettings();
        if (StartWithWindowsBox.IsChecked == true)
        {
            try
            {
                StartupManager.SetEnabled(true, StartMinimizedBox.IsChecked == true);
            }
            catch (Exception ex)
            {
                SetStatus($"Nao consegui atualizar a inicializacao: {ex.Message}");
            }
        }
    }

    private void ShowOverlayBox_Changed(object sender, RoutedEventArgs e)
    {
        if (suppressSettingsSave)
        {
            return;
        }

        settings.ShowVolumeOverlay = ShowOverlayBox.IsChecked == true;
        SaveSettings();
    }

    private AudioDeviceInfo? SelectedDevice => DeviceCombo.SelectedItem as AudioDeviceInfo;

    private AudioSessionInfo? SelectedSession => SessionGrid is null ? null : (SessionGrid.SelectedItem as SessionRow)?.Info;

    public sealed class SessionRow : INotifyPropertyChanged
    {
        internal SessionRow(AudioSessionInfo info)
        {
            Info = info;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        internal AudioSessionInfo Info { get; private set; }

        public string ProcessName => Info.ProcessName;

        public string ProcessIdText => Info.ProcessId == 0 ? "-" : Info.ProcessId.ToString(CultureInfo.InvariantCulture);

        public string State => Info.State;

        public string VolumeText => Info.Volume.ToString("P0", CultureInfo.CurrentCulture);

        public string SessionText => Info.DisplayName ?? Info.ShortSessionIdentifier;

        internal void Update(AudioSessionInfo info)
        {
            Info = info;
            OnPropertyChanged(string.Empty);
        }

        public void SetVolume(float volume)
        {
            Info = Info with { Volume = volume };
            OnPropertyChanged(nameof(VolumeText));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
