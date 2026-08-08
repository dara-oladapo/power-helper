using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Security;
using Microsoft.Win32;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PowerHelper.Tray;

/// <summary>
/// Renders short live text (e.g. "42m", "2h") directly onto the tray icon bitmap. The
/// Windows notification area has no API for a separate text region beside an icon - that's
/// a privileged Explorer-only affordance (the system clock) - so the only way for a
/// third-party app to show live text without a hover is to make the text the icon itself.
/// </summary>
public sealed class TrayIconRenderer
{
    private const int CanvasSize = 32;

    private readonly Color _textColor;
    private Icon? _current;

    public TrayIconRenderer()
    {
        _textColor = IsSystemUsingLightTaskbar() ? Color.Black : Color.White;
    }

    /// <summary>
    /// Renders new icon text and swaps it onto the given NotifyIcon, disposing the
    /// previously rendered icon only after the swap (the tray keeps its own copy once
    /// assigned, so the old one is safe to free once replaced, but not before).
    /// </summary>
    public void Apply(NotifyIcon trayIcon, string text)
    {
        var previous = _current;
        _current = Render(text);
        trayIcon.Icon = _current;
        previous?.Dispose();
    }

    private Icon Render(string text)
    {
        using var bitmap = new Bitmap(CanvasSize, CanvasSize, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            var fontSize = text.Length switch
            {
                <= 2 => 18f,
                3 => 15f,
                _ => 12f,
            };

            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(_textColor);
            var measured = g.MeasureString(text, font);
            g.DrawString(text, font, brush, (CanvasSize - measured.Width) / 2f, (CanvasSize - measured.Height) / 2f);
        }

        // Bitmap.GetHicon() does not preserve alpha correctly (transparent pixels come out
        // black) - building a real ICO container with an embedded PNG and loading that is
        // the reliable way to get a tray icon with a truly transparent background.
        return new Icon(BuildPngIconStream(bitmap));
    }

    private static MemoryStream BuildPngIconStream(Bitmap bitmap)
    {
        using var pngStream = new MemoryStream();
        bitmap.Save(pngStream, ImageFormat.Png);
        var pngBytes = pngStream.ToArray();

        var icoStream = new MemoryStream();
        using (var writer = new BinaryWriter(icoStream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((short)0);   // reserved
            writer.Write((short)1);   // type: icon
            writer.Write((short)1);   // image count

            writer.Write((byte)bitmap.Width);
            writer.Write((byte)bitmap.Height);
            writer.Write((byte)0);    // color count (0 = not palette-based)
            writer.Write((byte)0);    // reserved
            writer.Write((short)1);   // color planes
            writer.Write((short)32);  // bits per pixel
            writer.Write(pngBytes.Length);
            writer.Write(6 + 16);     // offset: header (6) + one directory entry (16)

            writer.Write(pngBytes);
        }

        icoStream.Position = 0;
        return icoStream;
    }

    private static bool IsSystemUsingLightTaskbar()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            // SystemUsesLightTheme governs taskbar/tray chrome specifically (separate from
            // AppsUseLightTheme, which only affects app windows).
            var value = key?.GetValue("SystemUsesLightTheme");
            return value is int i && i != 0;
        }
        catch (Exception ex) when (ex is SecurityException or IOException)
        {
            return false;
        }
    }

    public static void FormatCompactDuration(TimeSpan span, out string text)
    {
        text = span.TotalMinutes < 60
            ? $"{Math.Max(1, (int)span.TotalMinutes)}m"
            : $"{(int)Math.Round(span.TotalHours)}h";
    }
}
