using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace VolumeKeyRouter;

public sealed partial class VolumeOverlayWindow : Window
{
    private const int HideDelayMs = 1200;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int GwlExStyle = -20;
    private static readonly IntPtr HwndTopMost = new(-1);
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    private readonly DispatcherTimer hideTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(HideDelayMs)
    };
    private float currentVolume;

    public VolumeOverlayWindow()
    {
        InitializeComponent();
        BarTrack.SizeChanged += (_, _) => UpdateBarFill();
        hideTimer.Tick += (_, _) =>
        {
            hideTimer.Stop();
            Hide();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExNoActivate | WsExToolWindow);
    }

    public void ShowVolume(string target, float volume)
    {
        var clamped = Math.Clamp(volume, 0f, 1f);
        currentVolume = clamped;
        TargetText.Text = string.IsNullOrWhiteSpace(target) ? "Volume" : target;
        PercentText.Text = clamped.ToString("P0", CultureInfo.CurrentCulture);
        UpdateBarFill();

        PositionNearTaskbar();

        if (!IsVisible)
        {
            Show();
        }

        BringToTopWithoutActivation();
        hideTimer.Stop();
        hideTimer.Start();
    }

    private void UpdateBarFill()
    {
        var availableWidth = BarTrack.ActualWidth > 0 ? BarTrack.ActualWidth : 320;
        BarFill.Width = Math.Max(0, availableWidth * currentVolume);
    }

    private void PositionNearTaskbar()
    {
        var point = System.Windows.Forms.Cursor.Position;
        var screen = System.Windows.Forms.Screen.FromPoint(point).WorkingArea;

        var helper = new WindowInteropHelper(this);
        var handle = helper.Handle;
        if (handle == IntPtr.Zero)
        {
            handle = helper.EnsureHandle();
        }

        var source = HwndSource.FromHwnd(handle);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var topLeft = transform.Transform(new System.Windows.Point(screen.Left, screen.Top));
        var size = transform.Transform(new Vector(screen.Width, screen.Height));

        Left = topLeft.X + (size.X - Width) / 2;
        Top = topLeft.Y + size.Y - Height - 64;
    }

    private void BringToTopWithoutActivation()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            SetWindowPos(handle, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);
}
