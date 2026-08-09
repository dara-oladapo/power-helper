using PowerHelper.Abstractions;

namespace PowerHelper.Platform;

/// <summary>
/// A complete, honest set of platform services that can't do anything.
///
/// <para>
/// It exists for two reasons. It is the Linux implementation until there is a real one -
/// .NET MAUI has no Linux head at all, so Linux can't be a build target yet, but the
/// abstraction is proven by having a Linux-shaped thing satisfy it, and the work needed to
/// make it real is tracked as issues rather than as an empty folder.
/// </para>
///
/// <para>
/// It is also the fallback any head can compose with: a platform that supports four of the
/// seven capabilities takes its own four and these three, instead of writing three more
/// classes that each return false. Every stub reports <em>why</em>, so a user on that
/// platform reads a reason in the settings window rather than finding a switch that is
/// simply dead.
/// </para>
/// </summary>
public static class UnsupportedPlatform
{
    public const string LinuxNotImplementedYet =
        "Not available — Linux support hasn't been built yet. See the issues on GitHub.";

    public static PlatformServices Everything(string reason) => new(
        new UnsupportedGpuController(reason),
        new UnsupportedPowerProfileController(reason),
        new UnsupportedRefreshRateController(reason),
        new UnsupportedBrightnessController(reason),
        new UnknownBatteryReader(),
        new NoPowerSourceMonitor(),
        new UnsupportedStartupManager(reason));
}

public sealed class UnsupportedGpuController(string reason) : IGpuController
{
    public CapabilitySupport Support { get; } = CapabilitySupport.Unavailable(reason);

    public GpuState GetState() => GpuState.NotFound;

    public bool Enable() => false;

    public bool Disable() => false;
}

public sealed class UnsupportedPowerProfileController(string reason) : IPowerProfileController
{
    public CapabilitySupport Support { get; } = CapabilitySupport.Unavailable(reason);

    public string BatteryProfileName => "Power saver";

    public bool ApplyBatteryProfile() => false;

    public bool ApplyPluggedInProfile() => false;
}

public sealed class UnsupportedRefreshRateController(string reason) : IRefreshRateController
{
    public CapabilitySupport Support { get; } = CapabilitySupport.Unavailable(reason);

    public int NativeHertz => 60;

    public bool ThrottleToLowRate() => false;

    public bool RestoreNativeRate() => false;
}

public sealed class UnsupportedBrightnessController(string reason) : IBrightnessController
{
    public CapabilitySupport Support { get; } = CapabilitySupport.Unavailable(reason);

    public int? GetPercent() => null;

    public bool SetPercent(int percent) => false;
}

/// <summary>
/// Reports "no battery" rather than a fabricated percentage. A power utility that invents a
/// charge level is worse than one that admits it can't read one.
/// </summary>
public sealed class UnknownBatteryReader : IBatteryReader
{
    public BatteryStatus GetStatus() =>
        new(BatteryPresent: false, PercentCharged: 0, PluggedIn: true, Charging: false, TimeRemaining: null,
            Description: "Battery status unavailable on this platform");
}

/// <summary>
/// Assumes mains power. Every automatic policy is keyed off "am I on battery?", so this is
/// the answer that leaves the machine alone - the safe default when we genuinely don't know.
/// </summary>
public sealed class NoPowerSourceMonitor : IPowerSourceMonitor
{
    public event Action<bool>? PowerSourceChanged;

    public bool IsOnBattery() => false;

    public void Dispose() => PowerSourceChanged = null;
}

public sealed class UnsupportedStartupManager(string reason) : IStartupManager
{
    public CapabilitySupport Support { get; } = CapabilitySupport.Unavailable(reason);

    public bool IsRegistered() => false;

    public bool Register() => false;

    public bool Unregister() => false;
}
