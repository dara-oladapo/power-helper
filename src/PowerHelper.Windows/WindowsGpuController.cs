using System.Diagnostics;
using System.Management;
using PowerHelper.Abstractions;

namespace PowerHelper.Windows;

/// <summary>
/// Finds the discrete NVIDIA display adapter and enables/disables it at the PnP device
/// level via pnputil. Disabling the device (rather than e.g. an NVIDIA Optimus per-app
/// preference) is what makes this work regardless of which app is running - Windows has
/// no adapter to hand out once it's disabled.
/// </summary>
public sealed class WindowsGpuController : IGpuController
{
    private readonly string? _instanceId;

    public WindowsGpuController()
    {
        _instanceId = FindDiscreteGpuInstanceId();

        Support = _instanceId is not null
            ? CapabilitySupport.Supported
            : CapabilitySupport.Unavailable(
                "No discrete GPU was detected, so there's nothing to switch off. Every other feature here works without one.");
    }

    public CapabilitySupport Support { get; }

    // Not cached beyond construction for the state, but the instance id is: driver
    // updates/reinstalls can change it, though not while the app is running, and re-querying
    // WMI on every power transition is measurably slower than it is useful.
    private static string? FindDiscreteGpuInstanceId()
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
            // WMI unavailable or the query refused - indistinguishable from "no dGPU" as
            // far as what this app can do about it.
        }

        return null;
    }

    public GpuState GetState()
    {
        if (_instanceId is null)
        {
            return GpuState.NotFound;
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT ConfigManagerErrorCode FROM Win32_PnPEntity WHERE DeviceID = '{EscapeForWmi(_instanceId)}'");

            foreach (ManagementBaseObject device in searcher.Get())
            {
                var errorCode = Convert.ToInt32(device["ConfigManagerErrorCode"]);
                // CM_PROB_DISABLED = 22: the standard "this device is disabled" status code.
                return errorCode == 22 ? GpuState.Disabled : GpuState.Enabled;
            }
        }
        catch (ManagementException)
        {
            return GpuState.NotFound;
        }

        return GpuState.NotFound;
    }

    public bool Disable() => _instanceId is { } id && RunPnputil("disable-device", id);

    public bool Enable() => _instanceId is { } id && RunPnputil("enable-device", id);

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
        // (see WindowsPowerProfileController, where this was diagnosed against powercfg.exe).
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0;
    }
}
