using System.Diagnostics;
using PowerHelper.Abstractions;

namespace PowerHelper.Windows;

/// <summary>
/// Switches Windows' built-in power scheme via powercfg.exe. Deliberately not built on
/// the Lenovo GameZone WMI power-mode methods - SetSmartFanMode fails with
/// WBEM_E_INVALID_OBJECT on this hardware even fully elevated, most likely because
/// Set-methods on that WMI class are ACL-restricted to Lenovo's own signed services.
/// powercfg is the OS-level equivalent Windows' own Settings app uses and has none of that
/// dependency.
/// </summary>
public sealed class WindowsPowerProfileController : IPowerProfileController
{
    // Built-in scheme GUIDs are constant across all Windows installs (SCHEME_MIN/SCHEME_MAX
    // aliases in powercfg), even when hidden from `powercfg /list` because they're inactive.
    private const string PowerSaverGuid = "a1841308-3541-4fab-bc81-f71556f20b4a";
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";

    public CapabilitySupport Support => CapabilitySupport.Supported;

    public string BatteryProfileName => "Power saver";

    public bool ApplyBatteryProfile() => SetActiveScheme(PowerSaverGuid);

    /// <summary>
    /// Balanced, never High performance. Forcing High performance keeps the CPU's minimum
    /// clock state at 100% even at idle, which ramps the fan from sustained clock speed
    /// rather than from actual heat - audibly noisy for no thermal reason. Performance stays
    /// one Fn+Q or Windows Settings click away when it's actually wanted.
    /// </summary>
    public bool ApplyPluggedInProfile() => SetActiveScheme(BalancedGuid);

    private static bool SetActiveScheme(string guid)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powercfg.exe",
            ArgumentList = { "/setactive", guid },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return false;
        }

        // Draining the redirected streams before/while waiting is required here, not just
        // best practice: without it, later calls in the same process silently stopped
        // taking effect on this machine even though ExitCode read back as 0.
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0;
    }
}
