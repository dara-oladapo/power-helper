using System.Diagnostics;
using PowerHelper.Abstractions;

namespace PowerHelper.Windows;

/// <summary>
/// Registers Power Helper to launch at logon via a Scheduled Task rather than the classic
/// Run registry key. Kept as a task rather than switched back to the registry key mainly for
/// consistency with the GPU helper tasks (see WindowsGpuController), which do need
/// "highest privileges" - this one doesn't, since the app itself now runs unprivileged.
/// </summary>
public sealed class WindowsStartupManager : IStartupManager
{
    private const string TaskName = "PowerHelper";

    public CapabilitySupport Support => CapabilitySupport.Supported;

    public bool IsRegistered() => RunSchtasks($"/Query /TN \"{TaskName}\"");

    public bool Register()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            return false;
        }

        return RunSchtasks($"/Create /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\"\" /SC ONLOGON /F");
    }

    public bool Unregister() => RunSchtasks($"/Delete /TN \"{TaskName}\" /F");

    private static bool RunSchtasks(string arguments) =>
        ProcessRunner.RunAndWait(new ProcessStartInfo { FileName = "schtasks.exe", Arguments = arguments });
}
