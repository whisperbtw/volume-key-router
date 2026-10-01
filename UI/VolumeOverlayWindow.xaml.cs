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
    private const int DefaultHideDelayMs = 1200;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExTransparent = 0x00000020;
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
        Interval = TimeSpan.FromMilliseconds(DefaultHideDelayMs)
    };
    private float currentVolume;
    private int hideDelayMs = DefaultHideDelayMs;
    private string? currentMediaDetail;
    private int currentArtworkFingerprint;
    private int currentArtworkLength;
    private bool hasArtwork;
    private bool showArtwork = true;
    private OverlayPosition overlayPosition = OverlayPosition.BottomCenter;
    private OverlayTheme overlayTheme = OverlayTheme.Dark;

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

    internal void ShowNotification(string message, string? track, byte[]? artworkBytes = null)
    {
        ShowVolume(message, 0, detail: track, artworkBytes: artworkBytes);
        PercentText.Visibility = Visibility.Collapsed;
        BarBackground.Visibility = Visibility.Collapsed;
        hideTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(hideDelayMs, 2500));
    }

    internal void Configure(OverlaySettings settings)
    {
        settings.Normalize();
        Width = settings.Width;
        Height = settings.Height;
        ApplySizePreset(settings.SizePreset);
        hideDelayMs = settings.DurationMs;
        hideTimer.Interval = TimeSpan.FromMilliseconds(hideDelayMs);
        showArtwork = settings.ShowArtwork;
        overlayPosition = settings.Position;
        overlayTheme = settings.Theme;
        ApplyTheme();

        if (!showArtwork)
        {
            hasArtwork = false;
            currentArtworkFingerprint = 0;
            currentArtworkLength = 0;
            ArtworkFrame.Background = EmptyArtworkBrush;
            ArtworkFrame.Visibility = Visibility.Collapsed;
        }
        else if (!hasArtwork)
        {
            ArtworkFrame.Visibility = ShouldReserveArtworkSpace()
                ? Visibility.Hidden
                : Visibility.Collapsed;
        }

        if (IsVisible)
        {
            UpdateLayout();
            PositionNearTaskbar();
            BringToTopWithoutActivation();
        }
    }

    private void ApplySizePreset(OverlaySizePreset preset)
    {
        if (preset == OverlaySizePreset.VerySmall)
        {
            RootBorder.Padding = new Thickness(10, 9, 11, 9);
            RootBorder.CornerRadius = new CornerRadius(10);
            ArtworkFrame.Width = 48;
            ArtworkFrame.Height = 48;
            ArtworkFrame.Margin = new Thickness(0, 0, 8, 0);
            ArtworkFrame.CornerRadius = new CornerRadius(6);
            TopRow.Height = new GridLength(24);
            PercentColumn.Width = new GridLength(60);
            TargetText.FontSize = 11;
            PercentText.FontSize = 18;
            DetailText.FontSize = 11;
            DetailText.Margin = new Thickness(0, 0, 0, 4);
            BarBackground.Height = 8;
            BarBackground.Padding = new Thickness(1);
            BarBackground.CornerRadius = new CornerRadius(2);
            BarFill.CornerRadius = new CornerRadius(1);
            return;
        }

        RootBorder.Padding = new Thickness(16, 14, 18, 16);
        RootBorder.CornerRadius = new CornerRadius(14);
        ArtworkFrame.Width = 76;
        ArtworkFrame.Height = 76;
        ArtworkFrame.Margin = new Thickness(0, 0, 14, 0);
        ArtworkFrame.CornerRadius = new CornerRadius(8);
        TopRow.Height = new GridLength(34);
        PercentColumn.Width = new GridLength(94);
        TargetText.FontSize = 12;
        PercentText.FontSize = 24;
        DetailText.FontSize = 13;
        DetailText.Margin = new Thickness(0, 0, 0, 8);
        BarBackground.Height = 14;
        BarBackground.Padding = new Thickness(2);
        BarBackground.CornerRadius = new CornerRadius(3);
        BarFill.CornerRadius = new CornerRadius(2);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExNoActivate | WsExToolWindow | WsExTransparent);
    }

    public void ShowVolume(
        string target,
        float volume,
        string? detail = null,
        bool isMuted = false,
        byte[]? artworkBytes = null,
        bool preserveMedia = false)
    {
        PercentText.Visibility = Visibility.Visible;
        BarBackground.Visibility = Visibility.Visible;
        hideTimer.Interval = TimeSpan.FromMilliseconds(hideDelayMs);
        var clamped = Math.Clamp(volume, 0f, 1f);
        currentVolume = isMuted ? 0 : clamped;
        TargetText.Text = string.IsNullOrWhiteSpace(target) ? "Volume" : target;
        PercentText.Text = isMuted ? "MUDO" : clamped.ToString("P0", CultureInfo.CurrentCulture);
        BarFill.Background = isMuted ? MutedBrush : ActiveBrush;
        if (!preserveMedia)
        {
            UpdateMedia(detail, artworkBytes);
        }

        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        UpdateBarFill();
        PositionNearTaskbar();
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
        if (!showArtwork)
        {
            hasArtwork = false;
            currentArtworkFingerprint = 0;
            currentArtworkLength = 0;
            ArtworkFrame.Background = EmptyArtworkBrush;
            ArtworkFrame.Visibility = Visibility.Collapsed;
            return;
        }

        if (artworkBytes is null || artworkBytes.Length == 0)
        {
            hasArtwork = false;
            currentArtworkFingerprint = 0;
            currentArtworkLength = 0;
            ArtworkFrame.Background = EmptyArtworkBrush;
            ArtworkFrame.Visibility = ShouldReserveArtworkSpace()
                ? Visibility.Hidden
                : Visibility.Collapsed;
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
            ArtworkFrame.Visibility = ShouldReserveArtworkSpace()
                ? Visibility.Hidden
                : Visibility.Collapsed;
        }
    }

    private bool ShouldReserveArtworkSpace()
    {
        return showArtwork && currentMediaDetail is not null;
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

        const double edgeMargin = 28;
        const double taskbarMargin = 64;
        var windowWidth = GetEffectiveWidth();
        var windowHeight = GetEffectiveHeight();

        Left = overlayPosition switch
        {
            OverlayPosition.BottomLeft or OverlayPosition.TopLeft => topLeft.X + edgeMargin,
            OverlayPosition.BottomRight or OverlayPosition.TopRight => topLeft.X + size.X - windowWidth - edgeMargin,
            _ => topLeft.X + (size.X - windowWidth) / 2
        };
        Top = overlayPosition switch
        {
            OverlayPosition.TopCenter or OverlayPosition.TopLeft or OverlayPosition.TopRight => topLeft.Y + edgeMargin,
            _ => topLeft.Y + size.Y - windowHeight - taskbarMargin
        };
    }

    private double GetEffectiveWidth()
    {
        if (ActualWidth > 0)
        {
            return ActualWidth;
        }

        return double.IsNaN(Width) || Width <= 0 ? 430 : Width;
    }

    private double GetEffectiveHeight()
    {
        if (ActualHeight > 0)
        {
            return ActualHeight;
        }

        return double.IsNaN(Height) || Height <= 0 ? 136 : Height;
    }

    private void ApplyTheme()
    {
        if (overlayTheme == OverlayTheme.Light)
        {
            RootBorder.Background = new MediaSolidColorBrush(MediaColor.FromArgb(242, 246, 248, 252));
            TargetText.Foreground = new MediaSolidColorBrush(MediaColor.FromRgb(78, 86, 99));
            DetailText.Foreground = new MediaSolidColorBrush(MediaColor.FromRgb(18, 21, 27));
            PercentText.Foreground = new MediaSolidColorBrush(MediaColor.FromRgb(18, 21, 27));
            BarBackground.Background = new MediaSolidColorBrush(MediaColor.FromRgb(214, 220, 229));
            if (!hasArtwork)
            {
                ArtworkFrame.Background = new MediaSolidColorBrush(MediaColor.FromRgb(222, 227, 235));
            }

            return;
        }

        RootBorder.Background = new MediaSolidColorBrush(MediaColor.FromArgb(242, 18, 20, 25));
        TargetText.Foreground = new MediaSolidColorBrush(MediaColor.FromRgb(182, 190, 202));
        DetailText.Foreground = new MediaSolidColorBrush(MediaColor.FromRgb(242, 244, 248));
        PercentText.Foreground = new MediaSolidColorBrush(MediaColor.FromRgb(242, 244, 248));
        BarBackground.Background = new MediaSolidColorBrush(MediaColor.FromRgb(42, 47, 58));
        if (!hasArtwork)
        {
            ArtworkFrame.Background = EmptyArtworkBrush;
        }
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
