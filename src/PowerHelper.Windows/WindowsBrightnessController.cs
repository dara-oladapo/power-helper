using System.Management;
using PowerHelper.Abstractions;

namespace PowerHelper.Windows;

/// <summary>
/// Reads/sets the built-in panel's brightness via the standard WMI monitor-brightness
/// classes (root\wmi WmiMonitorBrightness / WmiMonitorBrightnessMethods) - the same
/// ACPI-backed mechanism Windows' own brightness slider uses. Unlike vendor WMI classes,
/// this is a public Windows class meant for third-party use and isn't ACL-restricted -
/// confirmed by invoking it directly, unelevated.
///
/// External monitors on HDMI/DisplayPort generally do not expose these classes, so this is
/// in practice a laptop-panel feature and reports itself unavailable elsewhere.
/// </summary>
public sealed class WindowsBrightnessController : IBrightnessController
{
    public WindowsBrightnessController()
    {
        Support = GetPercent() is not null
            ? CapabilitySupport.Supported
            : CapabilitySupport.Unavailable("Not available — this display doesn't expose brightness control through WMI.");
    }

    public CapabilitySupport Support { get; }

    public int? GetPercent()
    {
        try
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
        }
        catch (ManagementException)
        {
            // The class is absent on hardware that doesn't implement it, which is a
            // legitimate "no" rather than an error worth surfacing.
        }

        return null;
    }

    public bool SetPercent(int percent)
    {
        var clamped = Math.Clamp(percent, 0, 100);

        try
        {
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
        }
        catch (ManagementException)
        {
            return false;
        }

        return false;
    }
}
