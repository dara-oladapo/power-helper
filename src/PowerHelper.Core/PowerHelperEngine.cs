using PowerHelper.Abstractions;
using PowerHelper.Models;
using PowerHelper.Services;

namespace PowerHelper.Core;

public enum GpuActionResult
{
    Succeeded,
    Failed,
    Unsupported,
    Busy,
}

/// <summary>One consistent read of everything the tray and the window both display.</summary>
public readonly record struct StatusSnapshot(
    BatteryStatus Battery,
    GpuState Gpu,
    bool GpuPresent,
    bool OnBattery)
{
    public bool GpuEnabled => GpuPresent && Gpu != GpuState.Disabled;
}

/// <summary>
/// The single owner of settings, hardware policy and status polling, shared by every
/// surface the app has.
///
/// <para>
/// It knows nothing about which operating system it is running on. Everything that touches
/// a device arrives as <see cref="PlatformServices"/>, and every capability question is
/// answered by asking the implementation rather than by testing the platform - so a
/// half-ported OS degrades to disabled controls with reasons instead of to methods that
/// quietly return false.
/// </para>
///
/// <para>
/// Events are raised on whichever thread completed the work. Handlers are expected to
/// marshal to their own UI thread; the engine deliberately doesn't guess which that is,
/// because it has two consumers on two different message pumps.
/// </para>
/// </summary>
public sealed class PowerHelperEngine : IDisposable
{
    private const int ThrottledRefreshRateHertz = 60;

    // Slow enough to be invisible while the app sits in the tray, quick enough that a
    // percentage doesn't look frozen while someone is watching the window.
    public static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ActivePollInterval = TimeSpan.FromSeconds(5);

    private readonly PlatformServices _platform;
    private readonly SettingsService _settingsService = new();
    private readonly UpdateCheckService _updateCheckService = new();

    // Every device call and every status read passes through this. The platform
    // implementations are all slow enough to overlap - process launches, WMI, D-Bus - and a
    // battery reader that keeps rolling samples for a charge-rate estimate would have its
    // arithmetic corrupted by two concurrent readers.
    private readonly SemaphoreSlim _hardwareGate = new(1, 1);

    private readonly System.Threading.Timer _pollTimer;

    private bool _lowBatteryWarningShown;
    private bool _disposed;

    // The "unlock" half of the lock/unlock brightness feature: the level captured right
    // before capping it for battery, so returning to AC restores exactly what the user had
    // rather than a guessed default. Null whenever nothing is currently capped.
    private int? _brightnessBeforeCap;

    public AppSettings Settings { get; }

    public CapabilitySupport GpuSupport => _platform.Gpu.Support;

    public CapabilitySupport PowerProfileSupport => _platform.PowerProfile.Support;

    public CapabilitySupport RefreshRateSupport => _platform.RefreshRate.Support;

    public CapabilitySupport BrightnessSupport => _platform.Brightness.Support;

    public CapabilitySupport StartupSupport => _platform.Startup.Support;

    /// <summary>Names the battery power profile for the UI - "Power saver", "Low Power Mode".</summary>
    public string BatteryProfileName => _platform.PowerProfile.BatteryProfileName;

    /// <summary>
    /// Registering at logon is a process launch or a file write on every platform, so the
    /// answer is cached rather than re-queried. The previous code shelled out roughly once
    /// every three seconds for as long as the settings window stayed open.
    /// </summary>
    public bool StartupRegistered { get; private set; }

    public UpdateInfo? AvailableUpdate { get; private set; }

    public StatusSnapshot LastStatus { get; private set; }

    public event Action<StatusSnapshot>? StatusChanged;

    /// <summary>Raised when a setting is changed through one surface so the other can redraw.</summary>
    public event Action? SettingsChanged;

    public event Action<BatteryStatus>? LowBatteryWarning;

    public event Action<UpdateInfo>? UpdateFound;

    public PowerHelperEngine(PlatformServices platform)
    {
        _platform = platform;
        Settings = _settingsService.Load();
        StartupRegistered = _platform.Startup.Support.IsSupported && _platform.Startup.IsRegistered();

        // Synchronous on purpose: no surface should be presented before the automatic policy
        // has been applied once, or the app would briefly report a state it hasn't
        // actually established yet.
        ApplyDesiredStateCore(_platform.PowerSource.IsOnBattery());
        LastStatus = ReadSnapshot();

        _platform.PowerSource.PowerSourceChanged += OnPowerSourceChanged;

        // Power-source transitions trigger an immediate refresh, but charge % and
        // time-to-full/empty drift continuously in between, so poll too.
        _pollTimer = new System.Threading.Timer(_ => _ = RefreshStatusAsync(), null, IdlePollInterval, IdlePollInterval);
    }

    /// <summary>
    /// Polls faster while the window is on screen and backs off when it closes, rather
    /// than running one rate that is either wasteful or visibly stale.
    /// </summary>
    public void SetPollInterval(TimeSpan interval)
    {
        if (!_disposed)
        {
            _pollTimer.Change(interval, interval);
        }
    }

    // ---------------------------------------------------------------- status

    public async Task<StatusSnapshot> RefreshStatusAsync()
    {
        // A skipped tick costs nothing: another is always close behind, and waiting would
        // just queue reads against hardware that is already being read.
        if (!await _hardwareGate.WaitAsync(0).ConfigureAwait(false))
        {
            return LastStatus;
        }

        try
        {
            var snapshot = await Task.Run(ReadSnapshot).ConfigureAwait(false);
            PublishSnapshot(snapshot);
            return snapshot;
        }
        catch (Exception)
        {
            // A failed status read is cosmetic - the previous values stay on screen until
            // the next tick rather than taking the app down.
            return LastStatus;
        }
        finally
        {
            _hardwareGate.Release();
        }
    }

    private StatusSnapshot ReadSnapshot()
    {
        var battery = _platform.Battery.GetStatus();
        var onBattery = _platform.PowerSource.IsOnBattery();
        var gpuPresent = _platform.Gpu.Support.IsSupported;
        var gpu = gpuPresent ? _platform.Gpu.GetState() : GpuState.NotFound;
        return new StatusSnapshot(battery, gpu, gpuPresent, onBattery);
    }

    private void PublishSnapshot(StatusSnapshot snapshot)
    {
        LastStatus = snapshot;
        EvaluateLowBatteryWarning(snapshot.Battery);
        StatusChanged?.Invoke(snapshot);
    }

    private void EvaluateLowBatteryWarning(BatteryStatus battery)
    {
        if (!Settings.LowBatteryWarningEnabled || !battery.BatteryPresent || battery.PluggedIn)
        {
            _lowBatteryWarningShown = false;
            return;
        }

        if (!_lowBatteryWarningShown && battery.PercentCharged <= Settings.LowBatteryWarningThresholdPercent)
        {
            _lowBatteryWarningShown = true;
            LowBatteryWarning?.Invoke(battery);
        }
        // Hysteresis band above the threshold avoids re-alerting on every poll while
        // hovering right at the cutoff.
        else if (battery.PercentCharged > Settings.LowBatteryWarningThresholdPercent + 5)
        {
            _lowBatteryWarningShown = false;
        }
    }

    // ---------------------------------------------------------------- settings

    /// <summary>
    /// Persists whatever the caller just changed on <see cref="Settings"/>, tells the other
    /// surfaces, and re-applies the automatic policy in the background so no UI thread is
    /// ever held while a device call runs.
    /// </summary>
    public void NotifySettingsChanged()
    {
        _settingsService.Save(Settings);
        SettingsChanged?.Invoke();
        _ = ApplyDesiredStateAsync();
    }

    public async Task<bool> SetStartupAsync(bool enabled)
    {
        if (!_platform.Startup.Support.IsSupported)
        {
            return false;
        }

        var succeeded = await Task.Run(() => enabled ? _platform.Startup.Register() : _platform.Startup.Unregister())
            .ConfigureAwait(false);

        // Re-query rather than trust the action's own return value, so every surface
        // reflects what the OS actually did, not what we asked it to do.
        StartupRegistered = await Task.Run(_platform.Startup.IsRegistered).ConfigureAwait(false);
        SettingsChanged?.Invoke();
        return succeeded && StartupRegistered == enabled;
    }

    // ---------------------------------------------------------------- gpu

    /// <summary>
    /// A direct, one-off override of whatever the automatic policy last set - useful when
    /// the discrete GPU is needed right now despite being on battery. It is not sticky: the
    /// next power-source transition re-applies the automatic policy over this choice.
    /// </summary>
    public async Task<GpuActionResult> ToggleGpuManuallyAsync()
    {
        if (!_platform.Gpu.Support.IsSupported)
        {
            return GpuActionResult.Unsupported;
        }

        // A device switch takes long enough on real hardware that a second request arriving
        // mid-run would race the first and leave the adapter in whichever state finished last.
        if (!await _hardwareGate.WaitAsync(0).ConfigureAwait(false))
        {
            return GpuActionResult.Busy;
        }

        try
        {
            var succeeded = await Task.Run(() =>
                _platform.Gpu.GetState() == GpuState.Disabled
                    ? _platform.Gpu.Enable()
                    : _platform.Gpu.Disable()).ConfigureAwait(false);

            PublishSnapshot(await Task.Run(ReadSnapshot).ConfigureAwait(false));
            return succeeded ? GpuActionResult.Succeeded : GpuActionResult.Failed;
        }
        catch (Exception)
        {
            return GpuActionResult.Failed;
        }
        finally
        {
            _hardwareGate.Release();
        }
    }

    // ---------------------------------------------------------------- policy

    private void OnPowerSourceChanged(bool onBattery) => _ = ApplyDesiredStateAsync();

    /// <summary>
    /// Re-applies the automatic policy without blocking the caller. A request arriving while
    /// one is running is dropped rather than queued, because the run in flight reads
    /// <see cref="Settings"/> live and will already observe the newer values.
    /// </summary>
    public async Task ApplyDesiredStateAsync()
    {
        if (!await _hardwareGate.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var onBattery = _platform.PowerSource.IsOnBattery();
            await Task.Run(() => ApplyDesiredStateCore(onBattery)).ConfigureAwait(false);
            PublishSnapshot(await Task.Run(ReadSnapshot).ConfigureAwait(false));
        }
        catch (Exception)
        {
            // Implementations already report failure through their return values; a throw
            // here means something unexpected, and the next transition will retry.
        }
        finally
        {
            _hardwareGate.Release();
        }
    }

    private void ApplyDesiredStateCore(bool onBattery)
    {
        ApplyGpuState(onBattery);
        ApplyPowerProfile(onBattery);
        ApplyRefreshRate(onBattery);
        ApplyBrightness(onBattery);
    }

    private void ApplyGpuState(bool onBattery)
    {
        if (!_platform.Gpu.Support.IsSupported)
        {
            return;
        }

        var shouldDisable = Settings.AutoDisableDgpuOnBattery && onBattery;
        var currentState = _platform.Gpu.GetState();

        if (shouldDisable && currentState != GpuState.Disabled)
        {
            _platform.Gpu.Disable();
        }
        else if (!shouldDisable && currentState == GpuState.Disabled)
        {
            _platform.Gpu.Enable();
        }
    }

    private void ApplyPowerProfile(bool onBattery)
    {
        if (!Settings.AutoSwitchPowerPlanOnBattery || !_platform.PowerProfile.Support.IsSupported)
        {
            return;
        }

        if (onBattery)
        {
            _platform.PowerProfile.ApplyBatteryProfile();
        }
        else
        {
            // Never the platform's maximum-performance profile. On Windows that pins the
            // CPU's minimum clock state at 100% even at idle, which ramps the fan from
            // sustained clock speed rather than from actual heat. Performance stays a
            // manual choice the user makes in the OS.
            _platform.PowerProfile.ApplyPluggedInProfile();
        }
    }

    private void ApplyRefreshRate(bool onBattery)
    {
        if (!Settings.ThrottleRefreshRateOnBattery || !_platform.RefreshRate.Support.IsSupported)
        {
            return;
        }

        if (onBattery)
        {
            _platform.RefreshRate.ThrottleToLowRate();
        }
        else
        {
            _platform.RefreshRate.RestoreNativeRate();
        }
    }

    private void ApplyBrightness(bool onBattery)
    {
        if (!_platform.Brightness.Support.IsSupported)
        {
            return;
        }

        if (Settings.CapBrightnessOnBattery && onBattery)
        {
            // Lock: remember whatever the level was right before capping, but only on the
            // transition into a capped state - re-running this while already capped (e.g.
            // a periodic re-apply) must not overwrite the saved level with the capped value.
            _brightnessBeforeCap ??= _platform.Brightness.GetPercent();

            _platform.Brightness.SetPercent(Settings.BatteryBrightnessPercent);
        }
        else if (_brightnessBeforeCap is { } saved)
        {
            // Unlock: restore exactly what the user had, then clear so the next cap starts
            // from a fresh capture instead of restoring a now-stale remembered value.
            _platform.Brightness.SetPercent(saved);
            _brightnessBeforeCap = null;
        }
    }

    // ---------------------------------------------------------------- updates

    public async Task<UpdateInfo?> CheckForUpdatesAsync(Version currentVersion)
    {
        var update = await _updateCheckService.CheckForUpdateAsync(currentVersion).ConfigureAwait(false);
        if (update is { } found)
        {
            AvailableUpdate = found;
            UpdateFound?.Invoke(found);
        }

        return update;
    }

    // ---------------------------------------------------------------- shutdown

    /// <summary>
    /// Leaving the GPU disabled, the refresh rate throttled, or a battery-saver profile
    /// active with no running process to undo it would strand the user in that state, so a
    /// clean exit always restores. This is the app's central bargain - anything added to
    /// <see cref="ApplyDesiredStateCore"/> has to be undone here.
    /// </summary>
    public void RestoreOnExit()
    {
        if (_platform.Gpu.Support.IsSupported && _platform.Gpu.GetState() == GpuState.Disabled)
        {
            _platform.Gpu.Enable();
        }

        if (Settings.ThrottleRefreshRateOnBattery && _platform.RefreshRate.Support.IsSupported)
        {
            _platform.RefreshRate.RestoreNativeRate();
        }

        if (Settings.AutoSwitchPowerPlanOnBattery && _platform.PowerProfile.Support.IsSupported)
        {
            _platform.PowerProfile.ApplyPluggedInProfile();
        }

        if (_brightnessBeforeCap is { } savedBrightness)
        {
            _platform.Brightness.SetPercent(savedBrightness);
            _brightnessBeforeCap = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pollTimer.Dispose();
        _platform.PowerSource.PowerSourceChanged -= OnPowerSourceChanged;
        _platform.PowerSource.Dispose();
        _hardwareGate.Dispose();
    }

    /// <summary>The low rate every platform throttles to. 60 Hz is universally supported.</summary>
    public static int ThrottledRefreshRate => ThrottledRefreshRateHertz;
}
