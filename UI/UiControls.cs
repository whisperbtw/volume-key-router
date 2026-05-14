using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VolumeKeyRouter;

internal sealed class BufferedListView : ListView
{
    public BufferedListView()
    {
        DoubleBuffered = true;
    }
}

internal sealed class VolumeOverlayForm : Form
{
    private const int HideDelayMs = 1200;
    private readonly Label targetLabel = new();
    private readonly Label percentLabel = new();
    private readonly Panel barBackground = new();
    private readonly Panel barFill = new();
    private readonly System.Windows.Forms.Timer hideTimer = new();

    public VolumeOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Size = new Size(360, 104);
        BackColor = Color.FromArgb(18, 20, 25);
        ForeColor = Color.FromArgb(242, 244, 248);
        Opacity = 0.96;
        Padding = new Padding(18, 14, 18, 16);

        targetLabel.AutoSize = false;
        targetLabel.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
        targetLabel.ForeColor = Color.FromArgb(182, 190, 202);
        targetLabel.TextAlign = ContentAlignment.MiddleLeft;
        targetLabel.SetBounds(18, 14, 246, 24);
        Controls.Add(targetLabel);

        percentLabel.AutoSize = false;
        percentLabel.Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold);
        percentLabel.ForeColor = Color.White;
        percentLabel.TextAlign = ContentAlignment.MiddleRight;
        percentLabel.SetBounds(248, 10, 94, 36);
        Controls.Add(percentLabel);

        barBackground.BackColor = Color.FromArgb(42, 47, 58);
        barBackground.SetBounds(18, 62, 324, 14);
        barBackground.Padding = new Padding(2);
        Controls.Add(barBackground);

        barFill.BackColor = Color.FromArgb(38, 208, 124);
        barFill.SetBounds(2, 2, 0, 10);
        barBackground.Controls.Add(barFill);

        hideTimer.Interval = HideDelayMs;
        hideTimer.Tick += (_, _) =>
        {
            hideTimer.Stop();
            Hide();
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var createParams = base.CreateParams;
            createParams.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            createParams.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
            return createParams;
        }
    }

    public void ShowVolume(string target, float volume)
    {
        var clamped = Math.Clamp(volume, 0f, 1f);
        targetLabel.Text = string.IsNullOrWhiteSpace(target) ? "Volume" : target;
        percentLabel.Text = clamped.ToString("P0", CultureInfo.CurrentCulture);

        var availableWidth = Math.Max(0, barBackground.ClientSize.Width - 4);
        barFill.Width = (int)Math.Round(availableWidth * clamped);

        PositionNearTaskbar();

        if (!Visible)
        {
            Show();
        }

        TopMost = false;
        TopMost = true;
        hideTimer.Stop();
        hideTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            hideTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        using var path = CreateRoundedRectanglePath(ClientRectangle, 14);
        Region = new Region(path);
    }

    private void PositionNearTaskbar()
    {
        var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(
            screen.Left + (screen.Width - Width) / 2,
            screen.Bottom - Height - 64);
    }

    private static System.Drawing.Drawing2D.GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var diameter = radius * 2;
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class DarkToolStripRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color Back = Color.FromArgb(28, 28, 32);
    private static readonly Color Hover = Color.FromArgb(52, 52, 60);
    private static readonly Color Border = Color.FromArgb(70, 70, 78);

    public DarkToolStripRenderer()
        : base(new DarkColorTable())
    {
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        using var brush = new SolidBrush(e.Item.Selected ? Hover : Back);
        e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Border);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Back;
        public override Color ImageMarginGradientBegin => Back;
        public override Color ImageMarginGradientMiddle => Back;
        public override Color ImageMarginGradientEnd => Back;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Border;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
    }
}
