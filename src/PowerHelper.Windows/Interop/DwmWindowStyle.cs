using System.Runtime.InteropServices;

namespace PowerHelper.Interop;

/// <summary>
/// Window styling Windows will not infer on its own. Every call is best-effort:
/// <c>DwmSetWindowAttribute</c> returns a failure HRESULT for an attribute the running
/// build doesn't recognise rather than throwing, so an older Windows 10 build simply keeps
/// its square corners and light chrome - which is what the rest of that desktop looks like.
/// </summary>
public static class DwmWindowStyle
{
    // Renumbered between Windows 10 builds: 19 on 1809-1909, 20 from 2004 onward. Trying 20
    // first and falling back is the documented way to cover both.
    private const int DwmwaUseImmersiveDarkModePre20H1 = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;

    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    /// <summary>
    /// Without this a dark-mode window gets a white title bar bolted onto a dark body - the
    /// most obvious tell that an app is not following the system theme.
    /// </summary>
    public static void SetTitleBarTheme(IntPtr hwnd, bool dark)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var value = dark ? 1 : 0;
        if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModePre20H1, ref value, sizeof(int));
        }

        // The non-client area is not redrawn just because the attribute changed, so a theme
        // switch while the window is open would otherwise not take effect until it was
        // hidden and shown again. Ask for a frame change without moving or resizing it.
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    /// <summary>
    /// Windows 11 rounds modern menus for you, but a WinForms context menu renders as a
    /// plain popup and keeps square corners unless asked.
    /// </summary>
    public static void SetRoundedCorners(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var preference = DwmwcpRound;
        DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
    }
}
