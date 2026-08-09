namespace PowerHelper.Abstractions;

/// <summary>
/// Named CapabilitySupport rather than the more obvious FeatureSupport because
/// System.Windows.Forms already has a type by that name, and the Windows implementations
/// live in a project that imports it - two types called the same thing, one of which is
/// about assembly version checks, is a collision worth not having.
///
/// Every capability this app has is a per-OS question, and on most operating systems the
/// answer for at least one of them is "there is no API for that". So support is a value the
/// implementation reports, not something the caller infers from the platform it happens to
/// be running on.
///
/// <para>
/// The rule for an implementation: if you cannot do the thing, return
/// <see cref="CapabilitySupport.Unavailable"/> with a <see cref="CapabilitySupport.Reason"/>
/// written for the person reading it in the settings window - name what is missing, not
/// which class returned false. Never pretend to succeed.
/// </para>
/// </summary>
public readonly record struct CapabilitySupport(bool IsSupported, string? Reason)
{
    public static CapabilitySupport Supported { get; } = new(true, null);

    /// <param name="reason">
    /// Shown verbatim under the setting it disables, so write it for a user: "Not available —
    /// macOS has no API for switching a discrete GPU", not "GpuController returned false".
    /// </param>
    public static CapabilitySupport Unavailable(string reason) => new(false, reason);
}

public enum GpuState
{
    Enabled,
    Disabled,
    NotFound,
}

/// <summary>Switches the discrete GPU at the device level.</summary>
public interface IGpuController
{
    CapabilitySupport Support { get; }

    GpuState GetState();

    bool Enable();

    bool Disable();
}

/// <summary>
/// Moves the OS between its battery-saving and its normal power profile. Deliberately
/// named for the intent rather than for Windows' "power scheme": macOS expresses the same
/// idea as Low Power Mode, with no scheme GUIDs anywhere.
/// </summary>
public interface IPowerProfileController
{
    CapabilitySupport Support { get; }

    /// <summary>Names the profile pair for the UI, e.g. "Power saver" / "Low Power Mode".</summary>
    string BatteryProfileName { get; }

    bool ApplyBatteryProfile();

    bool ApplyPluggedInProfile();
}

public interface IRefreshRateController
{
    CapabilitySupport Support { get; }

    int NativeHertz { get; }

    bool ThrottleToLowRate();

    bool RestoreNativeRate();
}

public interface IBrightnessController
{
    CapabilitySupport Support { get; }

    int? GetPercent();

    bool SetPercent(int percent);
}

public readonly record struct BatteryStatus(
    bool BatteryPresent,
    int PercentCharged,
    bool PluggedIn,
    bool Charging,
    TimeSpan? TimeRemaining,
    string Description);

public interface IBatteryReader
{
    BatteryStatus GetStatus();
}

public interface IPowerSourceMonitor : IDisposable
{
    bool IsOnBattery();

    event Action<bool>? PowerSourceChanged;
}

/// <summary>Registers the app to launch at logon - a scheduled task, a LaunchAgent, a .desktop file.</summary>
public interface IStartupManager
{
    CapabilitySupport Support { get; }

    bool IsRegistered();

    bool Register();

    bool Unregister();
}

/// <summary>
/// The full set of platform services the engine drives. Grouped into one type so a new OS
/// head is a single registration rather than seven, and so a partial port is impossible to
/// write by accident - every member has to be answered, even if the answer is a stub.
/// </summary>
public sealed record PlatformServices(
    IGpuController Gpu,
    IPowerProfileController PowerProfile,
    IRefreshRateController RefreshRate,
    IBrightnessController Brightness,
    IBatteryReader Battery,
    IPowerSourceMonitor PowerSource,
    IStartupManager Startup);
