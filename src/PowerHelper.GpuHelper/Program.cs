using System.Diagnostics;
using System.Management;

// Invoked only via the PowerHelperGpuEnable / PowerHelperGpuDisable scheduled tasks (see
// WindowsGpuController), each of which bakes in exactly one literal argument. No device ID
// or other caller-controlled input ever reaches this process - it re-discovers the discrete
// GPU itself, the same way WindowsGpuController.FindDiscreteGpuInstanceId does, so the only
// thing an unprivileged caller can ever make this elevated binary do is switch that one
// specific device on or off.
if (args is not ["enable" or "disable"])
{
    return 2;
}

var action = args[0] == "enable" ? "enable-device" : "disable-device";

var instanceId = FindDiscreteGpuInstanceId();
if (instanceId is null)
{
    // No discrete GPU to act on - not an error, just nothing to do.
    return 0;
}

return RunPnputil(action, instanceId) ? 0 : 1;

static string? FindDiscreteGpuInstanceId()
{
    try
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
    }
    catch (ManagementException)
    {
        // WMI unavailable or the query refused - indistinguishable from "no dGPU" as far as
        // what this can do about it.
    }

    return null;
}

static bool RunPnputil(string action, string instanceId)
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

    // Redirected streams must be drained before/while waiting - see WindowsGpuController.
    process.StandardOutput.ReadToEnd();
    process.StandardError.ReadToEnd();
    process.WaitForExit();
    return process.ExitCode == 0;
}
