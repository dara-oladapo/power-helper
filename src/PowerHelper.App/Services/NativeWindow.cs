using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using PowerHelper.Interop;
using PowerHelper.Services;
using MauiWindow = Microsoft.Maui.Controls.Window;

namespace PowerHelper.App.Services;

/// <summary>
/// The handful of things a tray-first app needs from the native window that MAUI doesn't
/// surface: hiding it instead of destroying it, bringing it back to the foreground, and
/// tinting the title bar to match the app mode.
///
/// This project has exactly one target platform, so there are no <c>#if WINDOWS</c> guards
/// here - a second head would be a different app, not a different branch.
/// </summary>
internal static class NativeWindow
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    public static IntPtr GetHandle(MauiWindow window) =>
        window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native
            ? WinRT.Interop.WindowNative.GetWindowHandle(native)
            : IntPtr.Zero;

    public static AppWindow? Resolve(MauiWindow window)
    {
        var hwnd = GetHandle(window);
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(id);
    }

    /// <summary>
    /// Show, then explicitly foreground. <see cref="AppWindow.Show()"/> alone will restore a
    /// hidden window behind whatever the user is currently working in, which for a window
    /// summoned from the tray reads as the click having done nothing at all.
    /// </summary>
    public static void ShowAndFocus(MauiWindow window)
    {
        Resolve(window)?.Show();

        var hwnd = GetHandle(window);
        if (hwnd != IntPtr.Zero)
        {
            SetForegroundWindow(hwnd);
        }
    }

    public static void Hide(MauiWindow window) => Resolve(window)?.Hide();

    public static void ApplyTitleBarTheme(MauiWindow window) =>
        DwmWindowStyle.SetTitleBarTheme(
            GetHandle(window),
            SystemThemeService.CurrentTheme == AppThemeMode.Dark);
}
