using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Input;
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
    private const uint KeyEventKeyUp = 0x0002;
    private static readonly TimeSpan[] PeekMediaProbeDelays = { TimeSpan.Zero };
    private static readonly TimeSpan[] PlaybackMediaProbeDelays =
    {
        TimeSpan.FromMilliseconds(80),
        TimeSpan.FromMilliseconds(120),
        TimeSpan.FromMilliseconds(180)
    };
    private static readonly TimeSpan[] TrackMediaProbeDelays =
    {
        TimeSpan.FromMilliseconds(90),
        TimeSpan.FromMilliseconds(120),
        TimeSpan.FromMilliseconds(180),
        TimeSpan.FromMilliseconds(260)
    };

    private readonly EventWaitHandle? activationEvent;
    private readonly EventWaitHandle? shutdownEvent;
    private readonly CancellationTokenSource activationWatcherCancellation = new();
    private readonly AppSettings settings;
    private readonly AudioManager audioManager = new();
    private readonly MediaSessionInfoProvider mediaSessionInfoProvider = new();
    private readonly object targetGate = new();
    private readonly object applyGate = new();
    private readonly object mediaCacheGate = new();
    private readonly object shortcutGate = new();
    private readonly Forms.NotifyIcon trayIcon = new();
    private readonly Forms.ContextMenuStrip trayMenu = new();
    private readonly Forms.ToolStripMenuItem trayToggleCaptureItem = new();
    private readonly Forms.ToolStripMenuItem trayShowItem = new();
    private readonly Forms.ToolStripMenuItem trayShowOverlayItem = new();
    private readonly Forms.ToolStripMenuItem trayRefreshItem = new();
    private readonly Forms.ToolStripMenuItem trayProfilesItem = new();
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
    private bool suppressProfileSelectionChange;
    private bool restoringSavedTarget;
    private bool suppressSavedTargetCancel;
    private bool runtimeInitialized;
    private bool startHiddenToTray;
    private bool allowClose;
    private long overlayRequestId;
    private MediaTrackInfo? cachedMediaTrack;
    private DateTime cachedMediaTrackUtc;
    private DateTime lastMediaLookupUtc;
    private ShortcutSettings shortcutSnapshot = new();
    private System.Windows.Controls.Button? recordingShortcutButton;
    private volatile bool shortcutRecordingActive;

    private static readonly OverlayOption<OverlayPosition>[] OverlayPositionOptions =
    {
        new(OverlayPosition.BottomCenter, "Inferior central"),
        new(OverlayPosition.BottomRight, "Inferior direita"),
        new(OverlayPosition.BottomLeft, "Inferior esquerda"),
        new(OverlayPosition.TopCenter, "Superior central"),
        new(OverlayPosition.TopRight, "Superior direita"),
        new(OverlayPosition.TopLeft, "Superior esquerda")
    };

    private static readonly OverlayOption<OverlayTheme>[] OverlayThemeOptions =
    {
        new(OverlayTheme.Dark, "Escuro"),
        new(OverlayTheme.Light, "Claro")
    };

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
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Deactivated += (_, _) => CancelShortcutRecording();
        InitializeOptionControls();
        RefreshProfileControls();
        UpdateShortcutSnapshot();
        volumeOverlay.Configure(settings.Overlay);

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

    private void InitializeOptionControls()
    {
        OverlayPositionCombo.ItemsSource = OverlayPositionOptions;
        OverlayThemeCombo.ItemsSource = OverlayThemeOptions;
    }

    private void ApplySettingsToControls()
    {
        var previousSuppressSettingsSave = suppressSettingsSave;
        suppressSettingsSave = true;
        try
        {
            RefreshProfileControls();
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
            ApplyOverlaySettingsToControls();
            ApplyShortcutSettingsToControls();
        }
        finally
        {
            suppressSettingsSave = previousSuppressSettingsSave;
        }

        volumeOverlay.Configure(settings.Overlay);
        UpdateShortcutSnapshot();
    }

    private void RefreshProfileControls()
    {
        if (ProfileCombo is null || ProfileNameBox is null)
        {
            return;
        }

        var previousSuppress = suppressProfileSelectionChange;
        suppressProfileSelectionChange = true;
        try
        {
            ProfileCombo.ItemsSource = null;
            ProfileCombo.ItemsSource = settings.Profiles;
            ProfileCombo.SelectedItem = settings.ActiveProfile;
            ProfileNameBox.Text = settings.ActiveProfile?.Name ?? string.Empty;
        }
        finally
        {
            suppressProfileSelectionChange = previousSuppress;
        }

        UpdateTrayMenu();
    }

    private void ApplyOverlaySettingsToControls()
    {
        SelectOverlayOption(OverlayPositionCombo, OverlayPositionOptions, settings.Overlay.Position);
        SelectOverlayOption(OverlayThemeCombo, OverlayThemeOptions, settings.Overlay.Theme);
        OverlayWidthSlider.Value = settings.Overlay.Width;
        OverlayDurationSlider.Value = settings.Overlay.DurationMs;
        OverlayArtworkBox.IsChecked = settings.Overlay.ShowArtwork;
        UpdateOverlayValueLabels();
    }

    private void ApplyShortcutSettingsToControls()
    {
        SetShortcutButton(VolumeDownShortcutButton, settings.Shortcuts.VolumeDown);
        SetShortcutButton(VolumeUpShortcutButton, settings.Shortcuts.VolumeUp);
        SetShortcutButton(MuteShortcutButton, settings.Shortcuts.Mute);
        SetShortcutButton(PeekShortcutButton, settings.Shortcuts.PeekMedia);
        SetShortcutButton(PreviousShortcutButton, settings.Shortcuts.PreviousTrack);
        SetShortcutButton(NextShortcutButton, settings.Shortcuts.NextTrack);
        SetShortcutButton(PlayPauseShortcutButton, settings.Shortcuts.PlayPause);
        SetShortcutButton(StopShortcutButton, settings.Shortcuts.Stop);
        ShowMediaKeysOverlayBox.IsChecked = settings.Shortcuts.ShowOverlayOnMediaKeys;
    }

    private void CopyOverlayControlsToSettings()
    {
        settings.Overlay.Position = GetSelectedOverlayValue(
            OverlayPositionCombo,
            OverlayPositionOptions,
            OverlayPosition.BottomCenter);
        settings.Overlay.Theme = GetSelectedOverlayValue(
            OverlayThemeCombo,
            OverlayThemeOptions,
            OverlayTheme.Dark);
        settings.Overlay.Width = Math.Clamp((int)Math.Round(OverlayWidthSlider.Value), 340, 620);
        settings.Overlay.DurationMs = Math.Clamp((int)Math.Round(OverlayDurationSlider.Value), 600, 5000);
        settings.Overlay.ShowArtwork = OverlayArtworkBox.IsChecked == true;
        settings.Overlay.Normalize();
    }

    private void CopyShortcutControlsToSettings()
    {
        settings.Shortcuts.ApplyVolumeDown(GetSelectedShortcut(
            VolumeDownShortcutButton,
            KeyboardShortcutKeys.VolumeDown));
        settings.Shortcuts.ApplyVolumeUp(GetSelectedShortcut(
            VolumeUpShortcutButton,
            KeyboardShortcutKeys.VolumeUp));
        settings.Shortcuts.ApplyMute(GetSelectedShortcut(
            MuteShortcutButton,
            KeyboardShortcutKeys.VolumeMute));
        settings.Shortcuts.ApplyPeekMedia(GetSelectedShortcut(
            PeekShortcutButton,
            KeyboardShortcutKeys.LaunchMediaSelect));
        settings.Shortcuts.ApplyPreviousTrack(GetSelectedShortcut(
            PreviousShortcutButton,
            KeyboardShortcutKeys.MediaPreviousTrack));
        settings.Shortcuts.ApplyNextTrack(GetSelectedShortcut(
            NextShortcutButton,
            KeyboardShortcutKeys.MediaNextTrack));
        settings.Shortcuts.ApplyPlayPause(GetSelectedShortcut(
            PlayPauseShortcutButton,
            KeyboardShortcutKeys.MediaPlayPause));
        settings.Shortcuts.ApplyStop(GetSelectedShortcut(
            StopShortcutButton,
            KeyboardShortcutKeys.MediaStop));
        settings.Shortcuts.ShowOverlayOnMediaKeys = ShowMediaKeysOverlayBox.IsChecked == true;
        settings.Shortcuts.Normalize();
        UpdateShortcutSnapshot();
    }

    private void UpdateOverlayValueLabels()
    {
        if (OverlayWidthValueText is not null && OverlayWidthSlider is not null)
        {
            OverlayWidthValueText.Text = $"{Math.Round(OverlayWidthSlider.Value)}px";
        }

        if (OverlayDurationValueText is not null && OverlayDurationSlider is not null)
        {
            OverlayDurationValueText.Text = $"{Math.Round(OverlayDurationSlider.Value) / 1000:0.0}s";
        }
    }

    private void UpdateShortcutSnapshot()
    {
        lock (shortcutGate)
        {
            shortcutSnapshot = settings.Shortcuts.Clone();
        }
    }

    private ShortcutSettings GetShortcutSettings()
    {
        lock (shortcutGate)
        {
            return shortcutSnapshot.Clone();
        }
    }

    private void SetShortcutButton(System.Windows.Controls.Button button, ShortcutBinding binding)
    {
        var normalized = binding.Normalize(KeyboardShortcutKeys.None);
        button.Tag = normalized;
        button.Content = KeyboardShortcutKeys.GetDisplayName(normalized);
    }

    private ShortcutBinding GetSelectedShortcut(System.Windows.Controls.Button button, int fallback)
    {
        return button.Tag is ShortcutBinding binding
            ? binding.Normalize(fallback)
            : new ShortcutBinding(fallback, ShortcutModifiers.None);
    }

    private static void SelectOverlayOption<T>(
        System.Windows.Controls.ComboBox combo,
        IReadOnlyList<OverlayOption<T>> options,
        T value)
        where T : struct, Enum
    {
        combo.SelectedItem = options.FirstOrDefault(option => EqualityComparer<T>.Default.Equals(option.Value, value))
            ?? options.First();
    }

    private static T GetSelectedOverlayValue<T>(
        System.Windows.Controls.ComboBox combo,
        IReadOnlyList<OverlayOption<T>> options,
        T fallback)
        where T : struct, Enum
    {
        return combo.SelectedItem is OverlayOption<T> option
            ? option.Value
            : options.FirstOrDefault(option => EqualityComparer<T>.Default.Equals(option.Value, fallback))?.Value ?? fallback;
    }

    private IEnumerable<System.Windows.Controls.Button> GetShortcutButtons()
    {
        yield return VolumeDownShortcutButton;
        yield return VolumeUpShortcutButton;
        yield return MuteShortcutButton;
        yield return PeekShortcutButton;
        yield return PreviousShortcutButton;
        yield return NextShortcutButton;
        yield return PlayPauseShortcutButton;
        yield return StopShortcutButton;
    }

    private string? GetShortcutConflictText()
    {
        var duplicate = GetShortcutButtons()
            .Select(button => GetSelectedShortcut(button, KeyboardShortcutKeys.None))
            .Where(binding => binding.IsConfigured)
            .GroupBy(binding => binding)
            .FirstOrDefault(group => group.Count() > 1);

        return duplicate is null
            ? null
            : $"Atalho duplicado: {KeyboardShortcutKeys.GetDisplayName(duplicate.Key)}.";
    }

    private bool TrySaveShortcutButton(System.Windows.Controls.Button button, ShortcutBinding binding, string verb)
    {
        var normalized = binding.Normalize(KeyboardShortcutKeys.None);
        if (TryFindShortcutConflict(button, normalized, out var conflictButton))
        {
            SetStatus(
                $"Atalho ja usado em {GetShortcutButtonLabel(conflictButton)}: {KeyboardShortcutKeys.GetDisplayName(normalized)}.");
            return false;
        }

        SetShortcutButton(button, normalized);
        CopyShortcutControlsToSettings();
        SaveSettings();
        SetStatus($"{verb}: {KeyboardShortcutKeys.GetDisplayName(normalized)}.");
        return true;
    }

    private bool TryFindShortcutConflict(
        System.Windows.Controls.Button targetButton,
        ShortcutBinding binding,
        out System.Windows.Controls.Button conflictButton)
    {
        conflictButton = null!;
        if (!binding.IsConfigured)
        {
            return false;
        }

        foreach (var button in GetShortcutButtons())
        {
            if (ReferenceEquals(button, targetButton))
            {
                continue;
            }

            if (GetSelectedShortcut(button, KeyboardShortcutKeys.None) == binding)
            {
                conflictButton = button;
                return true;
            }
        }

        return false;
    }

    private static string GetShortcutButtonLabel(System.Windows.Controls.Button button)
    {
        return button.Name switch
        {
            "VolumeDownShortcutButton" => "Diminuir volume",
            "VolumeUpShortcutButton" => "Aumentar volume",
            "MuteShortcutButton" => "Mute",
            "PeekShortcutButton" => "Mostrar midia",
            "PreviousShortcutButton" => "Midia anterior",
            "NextShortcutButton" => "Proxima midia",
            "PlayPauseShortcutButton" => "Play/Pause",
            "StopShortcutButton" => "Parar midia",
            _ => "outro atalho"
        };
    }

    private void BeginShortcutRecording(System.Windows.Controls.Button button)
    {
        CancelShortcutRecording();
        recordingShortcutButton = button;
        shortcutRecordingActive = true;
        button.Content = "Pressione a combinacao";
        button.Focus();
        Keyboard.Focus(button);
        SetStatus("Pressione a combinacao desejada. Esc cancela; Backspace desativa.");
    }

    private void CompleteShortcutRecording(ShortcutBinding binding)
    {
        var button = recordingShortcutButton;
        if (button is null)
        {
            return;
        }

        recordingShortcutButton = null;
        shortcutRecordingActive = false;
        if (!TrySaveShortcutButton(button, binding, "Atalho gravado"))
        {
            button.Content = KeyboardShortcutKeys.GetDisplayName(GetSelectedShortcut(button, KeyboardShortcutKeys.None));
        }
    }

    private void CancelShortcutRecording()
    {
        if (recordingShortcutButton is null)
        {
            return;
        }

        var button = recordingShortcutButton;
        recordingShortcutButton = null;
        shortcutRecordingActive = false;
        button.Content = KeyboardShortcutKeys.GetDisplayName(GetSelectedShortcut(button, KeyboardShortcutKeys.None));
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (recordingShortcutButton is null)
        {
            return;
        }

        e.Handled = true;
        var key = GetPressedKey(e);
        if (key == Key.Escape)
        {
            CancelShortcutRecording();
            SetStatus("Gravacao de atalho cancelada.");
            return;
        }

        if (key is Key.Back or Key.Delete)
        {
            CompleteShortcutRecording(new ShortcutBinding(KeyboardShortcutKeys.None, ShortcutModifiers.None));
            return;
        }

        if (IsModifierKey(key))
        {
            SetStatus("Escolha uma tecla que nao seja apenas Ctrl, Shift, Alt ou Win.");
            return;
        }

        var virtualKeyCode = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKeyCode <= 0)
        {
            SetStatus("Nao consegui reconhecer essa tecla.");
            return;
        }

        CompleteShortcutRecording(new ShortcutBinding(
            virtualKeyCode,
            GetCurrentWpfShortcutModifiers()));
    }

    private static Key GetPressedKey(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.System)
        {
            return e.SystemKey;
        }

        if (e.Key == Key.ImeProcessed)
        {
            return e.ImeProcessedKey;
        }

        if (e.Key == Key.DeadCharProcessed)
        {
            return e.DeadCharProcessedKey;
        }

        return e.Key;
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl or
            Key.LeftShift or Key.RightShift or
            Key.LeftAlt or Key.RightAlt or
            Key.LWin or Key.RWin;
    }

    private static ShortcutModifiers GetCurrentWpfShortcutModifiers()
    {
        var modifiers = ShortcutModifiers.None;
        var wpfModifiers = Keyboard.Modifiers;
        if (wpfModifiers.HasFlag(ModifierKeys.Control))
        {
            modifiers |= ShortcutModifiers.Control;
        }

        if (wpfModifiers.HasFlag(ModifierKeys.Shift))
        {
            modifiers |= ShortcutModifiers.Shift;
        }

        if (wpfModifiers.HasFlag(ModifierKeys.Alt))
        {
            modifiers |= ShortcutModifiers.Alt;
        }

        if (wpfModifiers.HasFlag(ModifierKeys.Windows))
        {
            modifiers |= ShortcutModifiers.Win;
        }

        return modifiers;
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
            hook = new VolumeKeyHook(HandleVolumeKey, HasValidTarget, HandleMediaKey, GetShortcutSettings);
            hook.Install();
            captureActive = true;
            UpdateCaptureButton();
            if (persist)
            {
                settings.CaptureActive = true;
                SaveSettings();
            }

            SetStatus("Captura ativa. Os atalhos configurados estao controlando o alvo.");
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
        if (shortcutRecordingActive)
        {
            return false;
        }

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

                var requestId = Interlocked.Increment(ref overlayRequestId);
                var cachedTrack = GetCachedMediaTrack();

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
                            volumeOverlay.ShowVolume(
                                result.TargetLabel,
                                result.IsMuteCommand && result.IsMuted ? 0 : result.After,
                                cachedTrack?.DisplayText,
                                result.IsMuteCommand && result.IsMuted,
                                cachedTrack?.ArtworkBytes,
                                preserveMedia: cachedTrack is null);
                        }
                    }
                });

                if (result.ChangedSessions > 0 && ShouldRefreshMediaTrack())
                {
                    _ = UpdateOverlayMediaInfoAsync(snapshot.DisplayName, result, requestId);
                }
            }
            catch (Exception ex)
            {
                BeginInvokeSafe(() => SetStatus($"Erro ao ajustar volume: {ex.Message}"));
            }
        });

        return true;
    }

    private bool HandleMediaKey(MediaKeyCommand command)
    {
        if (shortcutRecordingActive)
        {
            return false;
        }

        SendMediaKeyToWindows(command);
        if (command == MediaKeyCommand.Peek || GetShortcutSettings().ShowOverlayOnMediaKeys)
        {
            _ = ShowCurrentMediaOverlayAfterManualMediaKeyAsync(command);
        }

        return true;
    }

    private static void SendMediaKeyToWindows(MediaKeyCommand command)
    {
        var virtualKeyCode = command switch
        {
            MediaKeyCommand.PreviousTrack => KeyboardShortcutKeys.MediaPreviousTrack,
            MediaKeyCommand.NextTrack => KeyboardShortcutKeys.MediaNextTrack,
            MediaKeyCommand.PlayPause => KeyboardShortcutKeys.MediaPlayPause,
            MediaKeyCommand.Stop => KeyboardShortcutKeys.MediaStop,
            _ => KeyboardShortcutKeys.None
        };

        if (virtualKeyCode == KeyboardShortcutKeys.None)
        {
            return;
        }

        NativeMethods.keybd_event((byte)virtualKeyCode, 0, 0, NativeMethods.VolumeKeyRouterInjectedKeyExtraInfo);
        NativeMethods.keybd_event((byte)virtualKeyCode, 0, KeyEventKeyUp, NativeMethods.VolumeKeyRouterInjectedKeyExtraInfo);
    }

    private async Task UpdateOverlayMediaInfoAsync(string preferredTarget, VolumeAdjustmentResult result, long requestId)
    {
        try
        {
            using var metadataTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(750));
            var mediaTrack = await mediaSessionInfoProvider.GetCurrentTrackAsync(preferredTarget, metadataTimeout.Token);
            if (mediaTrack is null)
            {
                return;
            }

            CacheMediaTrack(mediaTrack);

            BeginInvokeSafe(() =>
            {
                if (requestId != Interlocked.Read(ref overlayRequestId) || ShowOverlayBox.IsChecked != true)
                {
                    return;
                }

                volumeOverlay.ShowVolume(
                    result.TargetLabel,
                    result.IsMuteCommand && result.IsMuted ? 0 : result.After,
                    mediaTrack.DisplayText,
                    result.IsMuteCommand && result.IsMuted,
                    mediaTrack.ArtworkBytes);
            });
        }
        catch
        {
        }
    }

    private async Task ShowCurrentMediaOverlayAfterManualMediaKeyAsync(MediaKeyCommand command)
    {
        try
        {
            var requestId = Interlocked.Increment(ref overlayRequestId);
            var snapshot = GetTargetSnapshot();
            var cachedTrack = GetCachedMediaTrack();
            var probeDelays = GetManualMediaProbeDelays(command);
            MediaTrackInfo? lastTrack = null;

            for (var index = 0; index < probeDelays.Length; index++)
            {
                if (probeDelays[index] > TimeSpan.Zero)
                {
                    await Task.Delay(probeDelays[index]);
                }

                var mediaTrack = await GetFreshMediaTrackAsync(
                    snapshot.DisplayName,
                    includeArtwork: false,
                    timeout: TimeSpan.FromMilliseconds(280));
                if (mediaTrack is null)
                {
                    continue;
                }

                mediaTrack = ReuseCachedArtwork(mediaTrack);
                lastTrack = mediaTrack;

                if (command == MediaKeyCommand.Peek ||
                    ShouldUseManualMediaProbe(command, cachedTrack, mediaTrack) ||
                    index == probeDelays.Length - 1)
                {
                    CacheMediaTrack(mediaTrack);
                    ShowMediaOverlay(
                        requestId,
                        mediaTrack.OverlayTitle,
                        mediaTrack.DisplayText,
                        mediaTrack.ArtworkBytes,
                        GetCurrentTargetVolumeState(snapshot),
                        honorOverlaySetting: false);
                    _ = RefreshOverlayArtworkAsync(requestId, snapshot, mediaTrack, honorOverlaySetting: false);
                    return;
                }
            }

            lastTrack ??= cachedTrack;
            if (lastTrack is null)
            {
                ShowMediaOverlay(
                    requestId,
                    "Nada tocando",
                    null,
                    null,
                    GetCurrentTargetVolumeState(snapshot),
                    honorOverlaySetting: false);
                return;
            }

            CacheMediaTrack(lastTrack);
            ShowMediaOverlay(
                requestId,
                lastTrack.OverlayTitle,
                lastTrack.DisplayText,
                lastTrack.ArtworkBytes,
                GetCurrentTargetVolumeState(snapshot),
                honorOverlaySetting: false);
            _ = RefreshOverlayArtworkAsync(requestId, snapshot, lastTrack, honorOverlaySetting: false);
        }
        catch
        {
        }
    }

    private async Task ShowCurrentMediaOverlayAsync(bool force, bool honorOverlaySetting)
    {
        var requestId = Interlocked.Increment(ref overlayRequestId);
        var snapshot = GetTargetSnapshot();
        var mediaTrack = await GetFreshMediaTrackAsync(snapshot.DisplayName);
        mediaTrack ??= GetCachedMediaTrack();

        if (mediaTrack is null)
        {
            if (!force)
            {
                return;
            }

            ShowMediaOverlay(
                requestId,
                "Nada tocando",
                null,
                null,
                GetCurrentTargetVolumeState(snapshot),
                honorOverlaySetting);
            return;
        }

        CacheMediaTrack(mediaTrack);
        ShowMediaOverlay(
            requestId,
            mediaTrack.OverlayTitle,
            mediaTrack.DisplayText,
            mediaTrack.ArtworkBytes,
            GetCurrentTargetVolumeState(snapshot),
            honorOverlaySetting);
    }

    private async Task<MediaTrackInfo?> GetFreshMediaTrackAsync(
        string preferredTarget,
        bool includeArtwork = true,
        TimeSpan? timeout = null)
    {
        try
        {
            using var metadataTimeout = new CancellationTokenSource(
                timeout ?? TimeSpan.FromMilliseconds(includeArtwork ? 900 : 300));
            return await mediaSessionInfoProvider.GetCurrentTrackAsync(
                preferredTarget,
                metadataTimeout.Token,
                includeArtwork);
        }
        catch
        {
            return null;
        }
    }

    private async Task RefreshOverlayArtworkAsync(
        long requestId,
        TargetSnapshot snapshot,
        MediaTrackInfo mediaTrack,
        bool honorOverlaySetting)
    {
        if (mediaTrack.ArtworkBytes is not null && mediaTrack.ArtworkBytes.Length > 0)
        {
            return;
        }

        var freshTrack = await GetFreshMediaTrackAsync(
            snapshot.DisplayName,
            includeArtwork: true,
            timeout: TimeSpan.FromMilliseconds(800));
        if (freshTrack?.ArtworkBytes is null || !IsSameTrack(mediaTrack, freshTrack))
        {
            return;
        }

        CacheMediaTrack(freshTrack);
        ShowMediaOverlay(
            requestId,
            freshTrack.OverlayTitle,
            freshTrack.DisplayText,
            freshTrack.ArtworkBytes,
            GetCurrentTargetVolumeState(snapshot),
            honorOverlaySetting);
    }

    private static TimeSpan[] GetManualMediaProbeDelays(MediaKeyCommand command)
    {
        return command switch
        {
            MediaKeyCommand.Peek => PeekMediaProbeDelays,
            MediaKeyCommand.PlayPause or MediaKeyCommand.Stop => PlaybackMediaProbeDelays,
            _ => TrackMediaProbeDelays
        };
    }

    private bool ShouldUseManualMediaProbe(
        MediaKeyCommand command,
        MediaTrackInfo? previousTrack,
        MediaTrackInfo mediaTrack)
    {
        if (previousTrack is null)
        {
            return true;
        }

        return command switch
        {
            MediaKeyCommand.PreviousTrack or MediaKeyCommand.NextTrack => !IsSameTrack(previousTrack, mediaTrack),
            MediaKeyCommand.PlayPause or MediaKeyCommand.Stop => !IsSameTrack(previousTrack, mediaTrack) ||
                previousTrack.PlaybackState != mediaTrack.PlaybackState,
            _ => true
        };
    }

    private MediaTrackInfo ReuseCachedArtwork(MediaTrackInfo mediaTrack)
    {
        if (mediaTrack.ArtworkBytes is not null && mediaTrack.ArtworkBytes.Length > 0)
        {
            return mediaTrack;
        }

        var cachedTrack = GetCachedMediaTrack();
        return cachedTrack is not null &&
            cachedTrack.ArtworkBytes is not null &&
            IsSameTrack(cachedTrack, mediaTrack)
                ? mediaTrack with { ArtworkBytes = cachedTrack.ArtworkBytes }
                : mediaTrack;
    }

    private static bool IsSameTrack(MediaTrackInfo left, MediaTrackInfo right)
    {
        return string.Equals(left.Title, right.Title, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.Artist ?? string.Empty, right.Artist ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private void ShowMediaOverlay(
        long requestId,
        string title,
        string? detail,
        byte[]? artworkBytes,
        TargetVolumeState targetVolume,
        bool honorOverlaySetting)
    {
        BeginInvokeSafe(() =>
        {
            if (requestId != Interlocked.Read(ref overlayRequestId))
            {
                return;
            }

            if (honorOverlaySetting && ShowOverlayBox.IsChecked != true)
            {
                return;
            }

            volumeOverlay.ShowVolume(
                title,
                targetVolume.Volume,
                detail,
                targetVolume.IsMuted,
                artworkBytes);
        });
    }

    private TargetVolumeState GetCurrentTargetVolumeState(TargetSnapshot snapshot)
    {
        if (!snapshot.IsValid)
        {
            return new TargetVolumeState(false, 1, false, "Midia");
        }

        try
        {
            lock (applyGate)
            {
                var state = snapshot.Mode == TargetMode.Device
                    ? audioManager.GetEndpointVolumeState(snapshot.DeviceId)
                    : audioManager.GetSessionVolumeState(snapshot.DeviceId, snapshot.SessionTarget);

                return state.Found
                    ? state
                    : new TargetVolumeState(false, 1, false, snapshot.DisplayName);
            }
        }
        catch
        {
            return new TargetVolumeState(false, 1, false, snapshot.DisplayName);
        }
    }

    private MediaTrackInfo? GetCachedMediaTrack()
    {
        lock (mediaCacheGate)
        {
            return cachedMediaTrack is not null && DateTime.UtcNow - cachedMediaTrackUtc <= TimeSpan.FromSeconds(30)
                ? cachedMediaTrack
                : null;
        }
    }

    private bool ShouldRefreshMediaTrack()
    {
        lock (mediaCacheGate)
        {
            var now = DateTime.UtcNow;
            if (now - lastMediaLookupUtc < TimeSpan.FromMilliseconds(900))
            {
                return false;
            }

            lastMediaLookupUtc = now;
            return true;
        }
    }

    private void CacheMediaTrack(MediaTrackInfo mediaTrack)
    {
        lock (mediaCacheGate)
        {
            cachedMediaTrack = mediaTrack;
            cachedMediaTrackUtc = DateTime.UtcNow;
        }
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

        trayShowOverlayItem.Text = "Mostrar overlay agora";
        trayShowOverlayItem.Click += (_, _) => BeginInvokeSafe(() =>
        {
            _ = ShowCurrentMediaOverlayAsync(force: true, honorOverlaySetting: false);
        });

        trayToggleCaptureItem.Click += (_, _) => BeginInvokeSafe(ToggleCapture);

        trayRefreshItem.Text = "Recarregar dispositivos";
        trayRefreshItem.Click += (_, _) => BeginInvokeSafe(() =>
        {
            RestoreFromTray();
            RefreshDevices();
        });

        trayProfilesItem.Text = "Perfis";

        var exitItem = new Forms.ToolStripMenuItem("Sair", null, (_, _) => BeginInvokeSafe(ExitApplication));

        trayMenu.Items.Add(trayShowItem);
        trayMenu.Items.Add(trayShowOverlayItem);
        trayMenu.Items.Add(trayToggleCaptureItem);
        trayMenu.Items.Add(trayRefreshItem);
        trayMenu.Items.Add(trayProfilesItem);
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
        trayToggleCaptureItem.Text = captureActive ? "Pausar roteamento" : "Ativar roteamento";
        trayShowItem.Text = IsVisible && WindowState != WindowState.Minimized ? "Abrir" : "Mostrar janela";
        trayProfilesItem.DropDownItems.Clear();

        foreach (var profile in settings.Profiles)
        {
            var item = new Forms.ToolStripMenuItem(profile.Name)
            {
                Checked = profile.Id == settings.ActiveProfileId
            };
            item.Click += (_, _) => BeginInvokeSafe(() => ActivateProfile(profile.Id));
            trayProfilesItem.DropDownItems.Add(item);
        }

        trayProfilesItem.Enabled = trayProfilesItem.DropDownItems.Count > 0;
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
        CopyOverlayControlsToSettings();
        CopyShortcutControlsToSettings();
        settings.UpdateActiveProfileFromCurrent();
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

    private void OverlaySetting_Changed(object sender, RoutedEventArgs e)
    {
        if (suppressSettingsSave)
        {
            return;
        }

        CopyOverlayControlsToSettings();
        volumeOverlay.Configure(settings.Overlay);
        SaveSettings();
        SetStatus("Overlay atualizado.");
    }

    private void OverlaySlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateOverlayValueLabels();
        if (suppressSettingsSave)
        {
            return;
        }

        CopyOverlayControlsToSettings();
        volumeOverlay.Configure(settings.Overlay);
        SaveSettings();
    }

    private void ShortcutRecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button)
        {
            BeginShortcutRecording(button);
        }
    }

    private void ShortcutResetButton_Click(object sender, RoutedEventArgs e)
    {
        CancelShortcutRecording();
        if (sender is not FrameworkElement element ||
            TryGetShortcutResetTarget(element.Name) is not { } resetTarget)
        {
            return;
        }

        TrySaveShortcutButton(resetTarget.Button, resetTarget.Binding, "Atalho restaurado");
    }

    private void ShortcutSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (suppressSettingsSave)
        {
            return;
        }

        CopyShortcutControlsToSettings();
        SaveSettings();
        SetStatus(GetShortcutConflictText() ?? "Atalhos atualizados.");
    }

    private void ResetShortcutsButton_Click(object sender, RoutedEventArgs e)
    {
        CancelShortcutRecording();
        settings.Shortcuts = new ShortcutSettings();
        settings.Shortcuts.Normalize();

        var previousSuppress = suppressSettingsSave;
        suppressSettingsSave = true;
        try
        {
            ApplyShortcutSettingsToControls();
        }
        finally
        {
            suppressSettingsSave = previousSuppress;
        }

        UpdateShortcutSnapshot();
        SaveSettings();
        SetStatus("Atalhos restaurados.");
    }

    private (System.Windows.Controls.Button Button, ShortcutBinding Binding)? TryGetShortcutResetTarget(string resetButtonName)
    {
        return resetButtonName switch
        {
            nameof(VolumeDownShortcutResetButton) => (
                VolumeDownShortcutButton,
                new ShortcutBinding(KeyboardShortcutKeys.VolumeDown, ShortcutModifiers.None)),
            nameof(VolumeUpShortcutResetButton) => (
                VolumeUpShortcutButton,
                new ShortcutBinding(KeyboardShortcutKeys.VolumeUp, ShortcutModifiers.None)),
            nameof(MuteShortcutResetButton) => (
                MuteShortcutButton,
                new ShortcutBinding(KeyboardShortcutKeys.VolumeMute, ShortcutModifiers.None)),
            nameof(PeekShortcutResetButton) => (
                PeekShortcutButton,
                new ShortcutBinding(KeyboardShortcutKeys.LaunchMediaSelect, ShortcutModifiers.None)),
            nameof(PreviousShortcutResetButton) => (
                PreviousShortcutButton,
                new ShortcutBinding(KeyboardShortcutKeys.MediaPreviousTrack, ShortcutModifiers.None)),
            nameof(NextShortcutResetButton) => (
                NextShortcutButton,
                new ShortcutBinding(KeyboardShortcutKeys.MediaNextTrack, ShortcutModifiers.None)),
            nameof(PlayPauseShortcutResetButton) => (
                PlayPauseShortcutButton,
                new ShortcutBinding(KeyboardShortcutKeys.MediaPlayPause, ShortcutModifiers.None)),
            nameof(StopShortcutResetButton) => (
                StopShortcutButton,
                new ShortcutBinding(KeyboardShortcutKeys.MediaStop, ShortcutModifiers.None)),
            _ => null
        };
    }

    private void ProfileCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (suppressProfileSelectionChange || ProfileCombo.SelectedItem is not ProfileSettings profile)
        {
            return;
        }

        ActivateProfile(profile.Id);
    }

    private void SaveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var profile = settings.ActiveProfile;
        if (profile is null)
        {
            return;
        }

        var name = ProfileNameBox.Text.Trim();
        profile.Name = string.IsNullOrWhiteSpace(name) ? profile.Name : name;
        SaveSettings();
        RefreshProfileControls();
        SetStatus($"Perfil salvo: {profile.Name}.");
    }

    private void NewProfileButton_Click(object sender, RoutedEventArgs e)
    {
        CopyCurrentControlsToSettings();
        var profile = settings.AddProfileFromCurrent("Novo perfil");
        settings.Save();
        RefreshProfileControls();
        ApplySettingsToControls();
        SetStatus($"Perfil criado: {profile.Name}.");
    }

    private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var removedName = settings.ActiveProfile?.Name ?? "perfil";
        if (!settings.RemoveActiveProfile())
        {
            SetStatus("Mantenha pelo menos um perfil.");
            return;
        }

        ApplySettingsToControls();
        restoringSavedTarget = HasSavedTarget();
        suppressSavedTargetCancel = true;
        try
        {
            RefreshDevices(restoringSavedTarget);
        }
        finally
        {
            suppressSavedTargetCancel = false;
        }

        settings.Save();
        RefreshProfileControls();
        SetStatus($"Perfil excluido: {removedName}.");
    }

    private void ActivateProfile(string profileId)
    {
        var profile = settings.Profiles.FirstOrDefault(candidate => candidate.Id == profileId);
        if (profile is null || profile.Id == settings.ActiveProfileId)
        {
            RefreshProfileControls();
            return;
        }

        CopyCurrentControlsToSettings();
        settings.UpdateActiveProfileFromCurrent();
        settings.ActiveProfileId = profile.Id;
        settings.ApplyActiveProfileToCurrent();

        var previousSuppress = suppressSettingsSave;
        suppressSettingsSave = true;
        suppressSavedTargetCancel = true;
        try
        {
            ApplySettingsToControls();
            restoringSavedTarget = HasSavedTarget();
            RefreshDevices(restoringSavedTarget);
        }
        finally
        {
            suppressSavedTargetCancel = false;
            suppressSettingsSave = previousSuppress;
        }

        settings.Save();
        UpdateTargetSnapshot();
        RefreshProfileControls();
        SetStatus($"Perfil ativo: {profile.Name}.");
    }

    private AudioDeviceInfo? SelectedDevice => DeviceCombo.SelectedItem as AudioDeviceInfo;

    private AudioSessionInfo? SelectedSession => SessionGrid is null ? null : (SessionGrid.SelectedItem as SessionRow)?.Info;

    private sealed record OverlayOption<T>(T Value, string DisplayName)
        where T : struct, Enum
    {
        public override string ToString()
        {
            return DisplayName;
        }
    }

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
