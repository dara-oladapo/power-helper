using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using PowerHelper.Interop;
using PowerHelper.Services;

namespace PowerHelper.Tray;

/// <summary>
/// The colours the tray menu draws with, in whichever app mode Windows is currently in.
/// Values track WinUI's Fluent tokens so the menu sits beside the Windows Settings app
/// rather than looking like a different operating system.
/// </summary>
internal readonly record struct TrayPalette(
    Color Surface,
    Color Text,
    Color Secondary,
    Color Hover,
    Color Stroke)
{
    public static TrayPalette For(AppThemeMode theme) => theme == AppThemeMode.Dark
        ? new TrayPalette(
            Surface: Color.FromArgb(0x2B, 0x2B, 0x2B),
            Text: Color.FromArgb(0xFF, 0xFF, 0xFF),
            Secondary: Color.FromArgb(0xC5, 0xC5, 0xC5),
            Hover: Color.FromArgb(0x38, 0x38, 0x38),
            Stroke: Color.FromArgb(0x30, 0x30, 0x30))
        : new TrayPalette(
            Surface: Color.FromArgb(0xFF, 0xFF, 0xFF),
            Text: Color.FromArgb(0x1B, 0x1B, 0x1B),
            Secondary: Color.FromArgb(0x5D, 0x5D, 0x5D),
            Hover: Color.FromArgb(0xE9, 0xE9, 0xE9),
            Stroke: Color.FromArgb(0xE5, 0xE5, 0xE5));
}

/// <summary>
/// Paints the tray context menu in the Windows app mode.
///
/// A <see cref="ContextMenuStrip"/> renders in the classic light grey scheme regardless of
/// the user's theme - WinForms has no dark mode. The tray menu is this app's primary
/// surface (most sessions never open the window at all), so leaving it as a white slab on
/// a dark desktop is the more visible half of the problem, not the lesser one.
/// </summary>
internal static class TrayMenuTheme
{
    public static void Apply(ContextMenuStrip menu, TrayPalette palette)
    {
        menu.RenderMode = ToolStripRenderMode.Professional;
        menu.Renderer = new TrayMenuRenderer(palette);
        menu.BackColor = palette.Surface;
        menu.ForeColor = palette.Text;
        menu.ShowImageMargin = true;

        ApplyToItems(menu.Items, palette);

        // Windows 11 rounds every other menu on the desktop; a square-cornered one reads as
        // a leftover from an older app. Fails silently on Windows 10.
        if (menu.IsHandleCreated)
        {
            DwmWindowStyle.SetRoundedCorners(menu.Handle);
        }
    }

    private static void ApplyToItems(ToolStripItemCollection items, TrayPalette palette)
    {
        foreach (ToolStripItem item in items)
        {
            item.BackColor = palette.Surface;

            // The status lines at the top of the menu are labels, not commands. Giving them
            // the secondary text colour marks them as read-only without greying them out to
            // the point of failing contrast, which is what a disabled menu item does.
            item.ForeColor = item is ToolStripLabel ? palette.Secondary : palette.Text;

            if (item is ToolStripMenuItem { HasDropDownItems: true } parent)
            {
                parent.DropDown.BackColor = palette.Surface;
                parent.DropDown.ForeColor = palette.Text;
                parent.DropDown.Renderer = new TrayMenuRenderer(palette);
                ApplyToItems(parent.DropDownItems, palette);
            }
        }
    }

    private sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly Color _glyph;

        public TrayMenuRenderer(TrayPalette palette)
            : base(new TrayMenuColorTable(palette))
        {
            _glyph = palette.Text;
            RoundedEdges = false;
        }

        // The base renderer draws submenu arrows and checkmarks in a system colour that is
        // near-black whatever the theme, so both vanish against a dark menu. Redrawn here
        // in the palette's own text colour.
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item is { Enabled: false } ? Color.Gray : _glyph;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var bounds = e.ImageRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            var graphics = e.Graphics;
            var previousMode = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var pen = new Pen(_glyph, 1.6f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };

            // A plain two-stroke tick sized off the image rectangle, so it stays centred and
            // proportional at any DPI instead of assuming a 16px margin.
            var left = bounds.Left + (bounds.Width * 0.28f);
            var middle = bounds.Left + (bounds.Width * 0.44f);
            var right = bounds.Left + (bounds.Width * 0.74f);
            var top = bounds.Top + (bounds.Height * 0.34f);
            var elbow = bounds.Top + (bounds.Height * 0.62f);
            var bottom = bounds.Top + (bounds.Height * 0.50f);

            graphics.DrawLines(pen,
            [
                new PointF(left, bottom),
                new PointF(middle, elbow),
                new PointF(right, top),
            ]);

            graphics.SmoothingMode = previousMode;
        }
    }

    private sealed class TrayMenuColorTable(TrayPalette palette) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => palette.Surface;

        public override Color MenuBorder => palette.Stroke;

        public override Color MenuItemBorder => palette.Hover;

        public override Color MenuItemSelected => palette.Hover;

        public override Color MenuItemSelectedGradientBegin => palette.Hover;

        public override Color MenuItemSelectedGradientEnd => palette.Hover;

        public override Color MenuItemPressedGradientBegin => palette.Hover;

        public override Color MenuItemPressedGradientMiddle => palette.Hover;

        public override Color MenuItemPressedGradientEnd => palette.Hover;

        // The check "well" behind a ticked item. Flat rather than the default blue box -
        // the tick itself is the signal, and a filled box beside every enabled policy makes
        // the menu look permanently selected.
        public override Color CheckBackground => palette.Surface;

        public override Color CheckSelectedBackground => palette.Hover;

        public override Color CheckPressedBackground => palette.Hover;

        public override Color ImageMarginGradientBegin => palette.Surface;

        public override Color ImageMarginGradientMiddle => palette.Surface;

        public override Color ImageMarginGradientEnd => palette.Surface;

        public override Color SeparatorDark => palette.Stroke;

        public override Color SeparatorLight => palette.Stroke;
    }
}
