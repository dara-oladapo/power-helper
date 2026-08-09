using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using PowerHelper.Interop;
using MauiWindow = Microsoft.Maui.Controls.Window;

namespace PowerHelper.App.Platforms.Windows;

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

    /// <summary>
    /// Puts the window's non-client area and its WinUI content into the app's current theme.
    ///
    /// <para>
    /// Both halves take the answer rather than reading the OS for themselves: with an in-app
    /// override, what gets painted is not necessarily what Windows' own app mode is set to.
    /// <paramref name="requested"/> is the override itself, where <c>Unspecified</c> maps to
    /// <c>ElementTheme.Default</c> so WinUI keeps tracking the OS; <paramref name="dark"/> is
    /// that override already resolved against the OS, which is what the title bar needs
    /// because DWM has no "follow the system" state.
    /// </para>
    /// </summary>
    public static void ApplyTheme(MauiWindow window, AppTheme requested, bool dark)
    {
        DwmWindowStyle.SetTitleBarTheme(GetHandle(window), dark);

        // MAUI's own AppThemeBinding covers everything this app draws, but the chrome WinUI
        // paints for a Switch, a Slider, a Button or the theme picker's drop-down comes from
        // the element tree's RequestedTheme, which an app-level override has to reach.
        if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native
            && native.Content is Microsoft.UI.Xaml.FrameworkElement root)
        {
            root.RequestedTheme = requested switch
            {
                AppTheme.Light => Microsoft.UI.Xaml.ElementTheme.Light,
                AppTheme.Dark => Microsoft.UI.Xaml.ElementTheme.Dark,
                _ => Microsoft.UI.Xaml.ElementTheme.Default,
            };
        }
    }
}
