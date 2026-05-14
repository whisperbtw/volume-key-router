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
internal sealed partial class MainForm : Form
{
    private readonly EventWaitHandle? activationEvent;
    private readonly CancellationTokenSource activationWatcherCancellation = new();
    private readonly AppSettings settings;
    private readonly AudioManager audioManager = new();
    private readonly object targetGate = new();
    private readonly object applyGate = new();

    private readonly ComboBox deviceCombo = new();
    private readonly ListView sessionList = new BufferedListView();
    private readonly RadioButton appModeButton = new();
    private readonly RadioButton deviceModeButton = new();
    private readonly NumericUpDown stepPicker = new();
    private readonly Label stepValueLabel = new();
    private readonly Button refreshButton = new();
    private readonly Button captureButton = new();
    private readonly CheckBox startWithWindowsBox = new();
    private readonly CheckBox minimizeToTrayBox = new();
    private readonly CheckBox startMinimizedBox = new();
    private readonly CheckBox showOverlayBox = new();
    private readonly Label statusLabel = new();
    private readonly Label targetLabel = new();
    private readonly NotifyIcon trayIcon = new();
    private readonly ContextMenuStrip trayMenu = new();
    private readonly ToolStripMenuItem trayToggleCaptureItem = new();
    private readonly ToolStripMenuItem trayShowItem = new();
    private readonly Icon appIcon;
    private readonly VolumeOverlayForm volumeOverlay = new();
    private readonly System.Windows.Forms.Timer savedTargetSearchTimer = new();

    private VolumeKeyHook? hook;
    private TargetSnapshot targetSnapshot = TargetSnapshot.Invalid;
    private bool captureActive;
    private bool refreshing;
    private bool suppressSettingsSave;
    private bool restoringSavedTarget;
    private bool suppressSavedTargetCancel;
    private bool runtimeInitialized;
    private bool startHiddenToTray;
    private bool allowClose;

    private static readonly Color DarkBack = Color.FromArgb(14, 16, 20);
    private static readonly Color DarkPanel = Color.FromArgb(22, 25, 31);
    private static readonly Color DarkControl = Color.FromArgb(31, 35, 44);
    private static readonly Color DarkControlHover = Color.FromArgb(42, 48, 60);
    private static readonly Color DarkControlPressed = Color.FromArgb(52, 60, 74);
    private static readonly Color DarkBorder = Color.FromArgb(71, 79, 94);
    private static readonly Color DarkText = Color.FromArgb(242, 244, 248);
    private static readonly Color MutedText = Color.FromArgb(166, 174, 186);
    private static readonly Color Accent = Color.FromArgb(38, 208, 124);

    public MainForm(EventWaitHandle? activationEvent = null, bool startMinimized = false)
    {
        this.activationEvent = activationEvent;
        settings = AppSettings.Load();
        startHiddenToTray = startMinimized;
        appIcon = AppIconLoader.Load();
        Text = Program.AppDisplayName;
        Icon = appIcon;
        MinimumSize = new Size(760, 500);
        Size = new Size(940, 620);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);
        if (startHiddenToTray)
        {
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
        }

        savedTargetSearchTimer.Interval = 1500;
        savedTargetSearchTimer.Tick += (_, _) => TryRestoreSavedTarget();

        BuildLayout();
        BuildTray();
        ApplyDarkTheme(this);

        Load += (_, _) => InitializeRuntime();

        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized && minimizeToTrayBox.Checked)
            {
                HideToTray();
            }
        };

        FormClosing += (_, eventArgs) =>
        {
            if (!allowClose && minimizeToTrayBox.Checked)
            {
                eventArgs.Cancel = true;
                SaveSettings();
                HideToTray();
            }
        };

        FormClosed += (_, _) =>
        {
            SaveSettings();
            StopCapture(persist: false);
            activationWatcherCancellation.Cancel();
            activationEvent?.Set();
            activationWatcherCancellation.Dispose();
            savedTargetSearchTimer.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            trayMenu.Dispose();
            volumeOverlay.Dispose();
            appIcon.Dispose();
            audioManager.Dispose();
        };
    }
}
