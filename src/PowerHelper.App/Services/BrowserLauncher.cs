using System.Diagnostics;

namespace PowerHelper.App.Services;

internal static class BrowserLauncher
{
    /// <summary>
    /// Opens a URL in the user's browser. Uses the shell rather than MAUI's Launcher
    /// because this process runs elevated: a directly-elevated process handing a URL to
    /// the shell is the path that survives the integrity-level boundary between it and the
    /// (medium-integrity) default browser.
    /// </summary>
    public static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No default browser, or the shell refused the association. Nothing useful to
            // do about it, and never worth taking the app down for.
        }
    }
}
