using System.Management;

namespace PowerHelper.Services;

/// <summary>
/// Reads/sets the built-in panel's brightness via the standard WMI monitor-brightness
/// classes (root\wmi WmiMonitorBrightness / WmiMonitorBrightnessMethods) - the same
/// ACPI-backed mechanism Windows' own brightness slider uses. Unlike the Lenovo GameZone
/// WMI class (see PowerPlanService's comment), this is a public Windows class meant for
/// third-party use and isn't ACL-restricted - confirmed by invoking it directly, unelevated.
/// </summary>
public sealed class BrightnessService
{
    public int? GetBrightness()
    {
        using var searcher = new ManagementObjectSearcher("root\\wmi", "SELECT * FROM WmiMonitorBrightness");
        using var results = searcher.Get();
        foreach (ManagementObject instance in results)
        {
            using (instance)
            {
                return Convert.ToInt32(instance["CurrentBrightness"]);
            }
        }

        return null;
    }

    public bool SetBrightness(int percent)
    {
        var clamped = Math.Clamp(percent, 0, 100);

        using var searcher = new ManagementObjectSearcher("root\\wmi", "SELECT * FROM WmiMonitorBrightnessMethods");
        using var results = searcher.Get();
        foreach (ManagementObject instance in results)
        {
            using (instance)
            {
                using var inParams = instance.GetMethodParameters("WmiSetBrightness");
                inParams["Timeout"] = 0;
                inParams["Brightness"] = (byte)clamped;
                using var result = instance.InvokeMethod("WmiSetBrightness", inParams, null);
                return true;
            }
        }

        return false;
    }
}
