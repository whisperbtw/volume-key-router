using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaImageBrush = System.Windows.Media.ImageBrush;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;
using MediaStretch = System.Windows.Media.Stretch;

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
    private static readonly MediaBrush ActiveBrush = new MediaSolidColorBrush(MediaColor.FromRgb(38, 208, 124));
    private static readonly MediaBrush MutedBrush = new MediaSolidColorBrush(MediaColor.FromRgb(255, 107, 107));
    private static readonly MediaBrush EmptyArtworkBrush = new MediaSolidColorBrush(MediaColor.FromRgb(42, 47, 58));

    private readonly DispatcherTimer hideTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(HideDelayMs)
    };
    private float currentVolume;
    private string? currentMediaDetail;
    private int currentArtworkFingerprint;
    private int currentArtworkLength;
    private bool hasArtwork;

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

    public void ShowVolume(
        string target,
        float volume,
        string? detail = null,
        bool isMuted = false,
        byte[]? artworkBytes = null,
        bool preserveMedia = false)
    {
        var clamped = Math.Clamp(volume, 0f, 1f);
        currentVolume = isMuted ? 0 : clamped;
        TargetText.Text = string.IsNullOrWhiteSpace(target) ? "Volume" : target;
        PercentText.Text = isMuted ? "MUDO" : clamped.ToString("P0", CultureInfo.CurrentCulture);
        BarFill.Background = isMuted ? MutedBrush : ActiveBrush;
        if (!preserveMedia)
        {
            UpdateMedia(detail, artworkBytes);
        }

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

    private void UpdateMedia(string? detail, byte[]? artworkBytes)
    {
        var normalizedDetail = string.IsNullOrWhiteSpace(detail) ? null : detail;
        if (normalizedDetail == currentMediaDetail && ArtworkMatches(artworkBytes))
        {
            return;
        }

        currentMediaDetail = normalizedDetail;
        DetailText.Text = normalizedDetail ?? string.Empty;
        DetailText.Visibility = normalizedDetail is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateArtwork(artworkBytes);
    }

    private bool ArtworkMatches(byte[]? artworkBytes)
    {
        if (artworkBytes is null || artworkBytes.Length == 0)
        {
            return !hasArtwork;
        }

        var fingerprint = ComputeArtworkFingerprint(artworkBytes);
        return hasArtwork &&
            artworkBytes.Length == currentArtworkLength &&
            fingerprint == currentArtworkFingerprint;
    }

    private void UpdateArtwork(byte[]? artworkBytes)
    {
        if (artworkBytes is null || artworkBytes.Length == 0)
        {
            hasArtwork = false;
            currentArtworkFingerprint = 0;
            currentArtworkLength = 0;
            ArtworkFrame.Background = EmptyArtworkBrush;
            ArtworkFrame.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            using var imageStream = new MemoryStream(artworkBytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = imageStream;
            bitmap.EndInit();
            bitmap.Freeze();

            ArtworkFrame.Background = new MediaImageBrush(bitmap)
            {
                Stretch = MediaStretch.UniformToFill
            };
            hasArtwork = true;
            currentArtworkFingerprint = ComputeArtworkFingerprint(artworkBytes);
            currentArtworkLength = artworkBytes.Length;
            ArtworkFrame.Visibility = Visibility.Visible;
        }
        catch
        {
            hasArtwork = false;
            currentArtworkFingerprint = 0;
            currentArtworkLength = 0;
            ArtworkFrame.Background = EmptyArtworkBrush;
            ArtworkFrame.Visibility = Visibility.Collapsed;
        }
    }

    private static int ComputeArtworkFingerprint(byte[] artworkBytes)
    {
        unchecked
        {
            var hash = 17;
            foreach (var value in artworkBytes)
            {
                hash = (hash * 31) + value;
            }

            return hash;
        }
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
