using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Threading;
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
    // The device toggle itself needs admin rights; the app no longer runs elevated, so it is
    // delegated to a standalone helper exe run through these two pre-registered RunLevel
    // Highest scheduled tasks. Each task's own principal carries the elevation, so triggering
    // it via `schtasks /run` needs no UAC prompt from this (unprivileged) process. Two fixed
    // tasks rather than one parameterised task so no caller-supplied argument ever reaches
    // the elevated side - see PowerHelper.GpuHelper.
    private const string EnableTaskName = "PowerHelperGpuEnable";
    private const string DisableTaskName = "PowerHelperGpuDisable";

    private static readonly object HelperTaskLock = new();
    private static bool _helperTasksChecked;

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

    public bool Disable() => _instanceId is not null && RunHelperTask(DisableTaskName, GpuState.Disabled);

    public bool Enable() => _instanceId is not null && RunHelperTask(EnableTaskName, GpuState.Enabled);

    private static string EscapeForWmi(string value) => value.Replace("\\", "\\\\");

    private bool RunHelperTask(string taskName, GpuState desiredState)
    {
        EnsureHelperTasksRegistered();

        ProcessRunner.RunAndWait(new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = $"/Run /TN \"{taskName}\"",
        });

        // `schtasks /Run` starts the task asynchronously and doesn't hand back the task's own
        // exit code, so success is confirmed by re-reading the device state - the same
        // "trust what happened, not what was asked for" idiom PowerHelperEngine uses for
        // startup registration.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (GetState() == desiredState)
            {
                return true;
            }

            Thread.Sleep(150);
        }

        return GetState() == desiredState;
    }

    /// <summary>
    /// Creates the two helper scheduled tasks the first time this process needs them. An
    /// installer-based install already registers both (see installer/setup.iss), so in the
    /// common case this is just two quick `schtasks /Query` calls that find nothing to do.
    /// Running from source without the installer is the case this covers: task creation with
    /// RunLevel Highest itself needs elevation, so it is done once via a single UAC prompt on
    /// schtasks.exe alone, not on this process.
    /// </summary>
    private static void EnsureHelperTasksRegistered()
    {
        lock (HelperTaskLock)
        {
            if (_helperTasksChecked)
            {
                return;
            }

            _helperTasksChecked = true;

            if (TaskExists(EnableTaskName) && TaskExists(DisableTaskName))
            {
                return;
            }

            var helperPath = FindHelperExePath();
            if (helperPath is null)
            {
                return;
            }

            CreateTaskElevated(EnableTaskName, helperPath, "enable");
            CreateTaskElevated(DisableTaskName, helperPath, "disable");
        }
    }

    private static bool TaskExists(string taskName) => ProcessRunner.RunAndWait(new ProcessStartInfo
    {
        FileName = "schtasks.exe",
        Arguments = $"/Query /TN \"{taskName}\"",
    });

    private static void CreateTaskElevated(string taskName, string helperPath, string verbArg)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = $"/Create /TN \"{taskName}\" /TR \"\\\"{helperPath}\\\" {verbArg}\" /RL HIGHEST /F",
            UseShellExecute = true,
            Verb = "runas",
        };

        try
        {
            using var process = Process.Start(startInfo);
            process?.WaitForExit();
        }
        catch (Win32Exception)
        {
            // The user declined the UAC prompt. Enable()/Disable() will keep failing until
            // this succeeds on a later attempt - there's no silent workaround for "the user
            // said no to installing the one thing that needs admin rights".
        }
    }

    private static string? FindHelperExePath()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(exePath);
        if (directory is null)
        {
            return null;
        }

        var helperPath = Path.Combine(directory, "PowerHelper.GpuHelper.exe");
        return File.Exists(helperPath) ? helperPath : null;
    }
}
