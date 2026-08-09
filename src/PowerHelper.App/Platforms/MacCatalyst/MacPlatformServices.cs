using System.Diagnostics;
using PowerHelper.Abstractions;
using PowerHelper.Platform;

namespace PowerHelper.App.Platforms.MacCatalyst;

/// <summary>
/// Composes the macOS implementations into the bundle the engine takes.
///
/// <para>
/// Three of the seven capabilities are real on macOS and four are not, and the four are not
/// a porting backlog — they are things Apple does not expose to a third-party app at all.
/// Each reason below is written to be read by a user in the settings window, because that is
/// exactly where it ends up.
/// </para>
///
/// <list type="bullet">
/// <item><b>Discrete GPU</b> — Apple Silicon has no discrete GPU to switch. On the Intel
/// machines that did, the switch was never public API; the tools that managed it drove
/// private frameworks and stopped working across OS versions.</item>
/// <item><b>Low Power Mode</b> — <c>pmset -a lowpowermode 1</c> requires root. An
/// unprivileged app cannot set it, and macOS has no consent prompt that would grant it.</item>
/// <item><b>Brightness</b> — no public API reachable from Mac Catalyst. Apps that do this
/// use private DisplayServices calls.</item>
/// <item><b>Refresh rate</b> — likewise no public Catalyst API; ProMotion is managed by the
/// system.</item>
/// </list>
///
/// <para>
/// Reporting these honestly is the whole point of <see cref="CapabilitySupport"/>. The
/// alternative — a switch that looks live and silently does nothing — is worse than a
/// disabled one that explains itself.
/// </para>
/// </summary>
internal static class MacPlatformServices
{
    public static PlatformServices Create()
    {
        // Shared so one status refresh is a single pmset launch rather than two.
        var powerSource = new MacPowerSource();

        return new PlatformServices(
            Gpu: new UnsupportedGpuController(
                "Not available on macOS — Apple Silicon has no discrete GPU to switch, and macOS never exposed a public API for it on the Intel machines that did."),
            PowerProfile: new UnsupportedPowerProfileController(
                "Not available on macOS — switching Low Power Mode requires root, which an app can't ask for. You can toggle it in System Settings › Battery."),
            RefreshRate: new UnsupportedRefreshRateController(
                "Not available on macOS — there's no public API for changing the display mode, and ProMotion already varies the rate on its own."),
            Brightness: new UnsupportedBrightnessController(
                "Not available on macOS — brightness isn't reachable from a Mac Catalyst app without private APIs."),
            Battery: new MacBatteryReader(powerSource),
            PowerSource: new MacPowerSourceMonitor(powerSource),
            Startup: new MacStartupManager());
    }
}

/// <summary>
/// Registers a per-user LaunchAgent, which is the macOS equivalent of the Windows logon
/// task — and unlike that one, needs no elevation, because nothing this app does on macOS
/// needs elevation either.
/// </summary>
internal sealed class MacStartupManager : IStartupManager
{
    private const string Label = "com.daraoladapo.powerhelper";

    private static string AgentPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library",
        "LaunchAgents",
        $"{Label}.plist");

    public CapabilitySupport Support => CapabilitySupport.Supported;

    public bool IsRegistered() => File.Exists(AgentPath);

    public bool Register()
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable))
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(AgentPath)!);
            File.WriteAllText(AgentPath, BuildPlist(executable));

            // Written and then loaded: without launchctl the agent only takes effect at the
            // next login, which makes the switch look like it didn't work.
            RunLaunchctl("load", AgentPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool Unregister()
    {
        try
        {
            if (!File.Exists(AgentPath))
            {
                return true;
            }

            RunLaunchctl("unload", AgentPath);
            File.Delete(AgentPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string BuildPlist(string executable) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
            <key>Label</key>
            <string>{Label}</string>
            <key>ProgramArguments</key>
            <array>
                <string>{System.Security.SecurityElement.Escape(executable)}</string>
            </array>
            <key>RunAtLoad</key>
            <true/>
        </dict>
        </plist>
        """;

    private static void RunLaunchctl(string verb, string path)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/launchctl",
                ArgumentList = { verb, path },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (process is null)
            {
                return;
            }

            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
        }
        catch (Exception)
        {
            // The plist is already on disk either way, so the agent still takes effect at
            // the next login. Best-effort is the right level for this.
        }
    }
}
