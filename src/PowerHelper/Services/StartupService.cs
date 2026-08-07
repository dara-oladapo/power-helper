using System.Diagnostics;

namespace PowerHelper.Services;

/// <summary>
/// Registers PowerHelper to launch at logon via a Scheduled Task rather than the
/// classic Run registry key. A task set to run "with highest privileges" launches
/// elevated without a UAC prompt, which the registry key approach cannot do - and
/// the app needs admin rights every time to enable/disable the GPU device.
/// </summary>
public sealed class StartupService
{
    private const string TaskName = "PowerHelper";

    public bool IsRegistered()
    {
        var result = RunSchtasks($"/Query /TN \"{TaskName}\"");
        return result;
    }

    public bool Register()
    {
        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the running executable path.");

        return RunSchtasks(
            $"/Create /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\"\" /SC ONLOGON /RL HIGHEST /F");
    }

    public bool Unregister() => RunSchtasks($"/Delete /TN \"{TaskName}\" /F");

    private static bool RunSchtasks(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = arguments,
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

        // Redirected streams must be drained before/while waiting - leaving them unread
        // has caused later calls in the same process to silently stop taking effect
        // (see PowerPlanService, where this was diagnosed against powercfg.exe).
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0;
    }
}
