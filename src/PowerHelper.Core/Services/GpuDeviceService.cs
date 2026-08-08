using System.Diagnostics;
using System.Management;

namespace PowerHelper.Services;

public enum GpuState
{
    Enabled,
    Disabled,
    NotFound,
}

/// <summary>
/// Finds the discrete NVIDIA display adapter and enables/disables it at the PnP device
/// level via pnputil. Disabling the device (rather than e.g. an NVIDIA Optimus per-app
/// preference) is what makes this work regardless of which app is running - Windows has
/// no adapter to hand out once it's disabled.
/// </summary>
public sealed class GpuDeviceService
{
    // Not cached: driver updates/reinstalls can change the PNP device instance id, and
    // this only runs on power-state transitions, so re-querying WMI each time is cheap
    // relative to the safety of always targeting the right device.
    public string? FindDiscreteGpuInstanceId()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT DeviceID, Name, Manufacturer FROM Win32_PnPEntity WHERE PNPClass = 'Display'");

        foreach (ManagementBaseObject device in searcher.Get())
        {
            var name = device["Name"] as string ?? string.Empty;
            var manufacturer = device["Manufacturer"] as string ?? string.Empty;

            if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
                || manufacturer.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
            {
                return device["DeviceID"] as string;
            }
        }

        return null;
    }

    public GpuState GetState(string instanceId)
    {
        using var searcher = new ManagementObjectSearcher(
            $"SELECT ConfigManagerErrorCode FROM Win32_PnPEntity WHERE DeviceID = '{EscapeForWmi(instanceId)}'");

        foreach (ManagementBaseObject device in searcher.Get())
        {
            var errorCode = Convert.ToInt32(device["ConfigManagerErrorCode"]);
            // CM_PROB_DISABLED = 22: the standard "this device is disabled" status code.
            return errorCode == 22 ? GpuState.Disabled : GpuState.Enabled;
        }

        return GpuState.NotFound;
    }

    public bool Disable(string instanceId) => RunPnputil("disable-device", instanceId);

    public bool Enable(string instanceId) => RunPnputil("enable-device", instanceId);

    private static string EscapeForWmi(string value) => value.Replace("\\", "\\\\");

    private static bool RunPnputil(string action, string instanceId)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "pnputil.exe",
            ArgumentList = { $"/{action}", instanceId },
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
