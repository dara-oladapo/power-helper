using System.Drawing;
using Microsoft.Win32;

namespace PowerHelper.Windows;

public enum AppThemeMode
{
    Light,
    Dark,
}

/// <summary>
/// Reads the two personalisation choices Windows exposes to third-party apps - the
/// light/dark app mode and the accent colour - and raises an event when either changes.
///
/// Deliberately registry-backed rather than WinRT (Windows.UI.ViewManagement.UISettings):
/// this project targets net10.0-windows rather than a pinned Windows SDK version, and
/// these two registry locations have been stable since Windows 10 1809.
///
/// Colours come back as <see cref="Color"/> because this project has no UI framework of
/// its own. The tray uses them directly; the MAUI app converts once at the boundary.
/// </summary>
public sealed class SystemThemeService : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";

    // Windows' own default accent ("Blue"), used whenever the palette can't be read or
    // doesn't look like what we expect. Two values because the accent Windows paints on a
    // dark surface is a lighter shade of the same hue than the one it paints on a light
    // surface - using one for both gives a washed-out accent in one theme and an
    // unreadable one in the other.
    private static readonly Color DefaultAccentOnLight = Color.FromArgb(0x00, 0x5A, 0x9E);
    private static readonly Color DefaultAccentOnDark = Color.FromArgb(0x4C, 0xC2, 0xFF);

    /// <summary>
    /// Raised when the user changes their app mode or accent colour. Not guaranteed to
    /// arrive on any particular thread - handlers marshal for themselves.
    /// </summary>
    public event Action? Changed;

    public SystemThemeService()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public static AppThemeMode CurrentTheme
    {
        get
        {
            // AppsUseLightTheme is the app-mode setting, distinct from SystemUsesLightTheme
            // (taskbar/Start, which is what the tray icon renderer reads). An app window
            // should follow the former. A missing value means an old build or a locked-down
            // policy; light is the safer assumption.
            var value = ReadDword(PersonalizeKey, "AppsUseLightTheme");
            return value == 0 ? AppThemeMode.Dark : AppThemeMode.Light;
        }
    }

    public static Color CurrentAccent(AppThemeMode theme) =>
        ReadAccentFromPalette(theme)
            ?? (theme == AppThemeMode.Dark ? DefaultAccentOnDark : DefaultAccentOnLight);

    /// <summary>
    /// AccentPalette is 8 consecutive RGBA quads ordered lightest to darkest: Light3,
    /// Light2, Light1, Accent, Dark1, Dark2, Dark3, and a trailing entry. Windows itself
    /// draws accent-on-dark with Light2 and accent-on-light with Dark1 rather than the base
    /// accent, because the base is tuned for large filled areas and is frequently too dark
    /// or too light to carry text at the opposite end.
    ///
    /// Returns null - rather than a guess - whenever the blob isn't the shape expected, so
    /// a future layout change degrades to Windows' default accent instead of rendering a
    /// colour assembled from misread bytes.
    /// </summary>
    private static Color? ReadAccentFromPalette(AppThemeMode theme)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AccentKey);
            if (key?.GetValue("AccentPalette") is not byte[] palette || palette.Length != 32)
            {
                return null;
            }

            var offset = theme == AppThemeMode.Dark ? 4 : 16;
            var (r, g, b, a) = (palette[offset], palette[offset + 1], palette[offset + 2], palette[offset + 3]);

            // Every entry in the palette is fully opaque. If this byte isn't 0xFF the
            // channel order has been misidentified, so fall back rather than show a wrong hue.
            return a == 0xFF ? Color.FromArgb(r, g, b) : null;
        }
        catch (Exception)
        {
            // A personalisation read is never worth taking the app down for.
            return null;
        }
    }

    private static int? ReadDword(string subKey, string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey);
            return key?.GetValue(name) as int?;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Theme and accent changes both arrive under General. It is a broad category, so
        // this fires more often than strictly needed - re-reading two registry values is
        // cheap enough that filtering more precisely isn't worth the fragility.
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
        {
            Changed?.Invoke();
        }
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
