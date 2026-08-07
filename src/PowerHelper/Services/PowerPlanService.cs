using System.Diagnostics;

namespace PowerHelper.Services;

/// <summary>
/// Switches Windows' built-in power scheme via powercfg.exe. Deliberately not built on
/// the Lenovo GameZone WMI power-mode methods (see PowerModeService) - SetSmartFanMode
/// fails with WBEM_E_INVALID_OBJECT on this hardware even fully elevated, most likely
/// because Set-methods on that WMI class are ACL-restricted to Lenovo's own signed
/// services. powercfg is the OS-level equivalent Windows' own Settings app uses and has
/// none of that dependency.
/// </summary>
public sealed class PowerPlanService
{
    // Built-in scheme GUIDs are constant across all Windows installs (SCHEME_MIN/SCHEME_MAX
    // aliases in powercfg), even when hidden from `powercfg /list` because they're inactive.
    private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string PowerSaverGuid = "a1841308-3541-4fab-bc81-f71556f20b4a";
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";

    public bool SetHighPerformance() => SetActiveScheme(HighPerformanceGuid);

    public bool SetPowerSaver() => SetActiveScheme(PowerSaverGuid);

    // Used to restore Windows' own default on exit, rather than leaving High performance
    // active if the app happened to quit while on AC.
    public bool SetBalanced() => SetActiveScheme(BalancedGuid);

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
