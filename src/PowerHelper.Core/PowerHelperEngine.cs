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
/// The single owner of settings, hardware policy and status polling, shared by the tray
/// icon and the settings window.
///
/// It exists because those two surfaces run on different threads with different message
/// pumps - the tray on its own WinForms loop, the window on the MAUI/WinUI dispatcher -
/// and previously each drove the hardware directly and then tried to keep the other in
/// step. Everything that touches a device now goes through here, serialised behind one
/// gate, and both surfaces render from the events it raises.
///
/// Events are raised on whichever thread completed the work. Handlers are expected to
/// marshal to their own UI thread; the engine deliberately doesn't guess which that is.
/// </summary>
public sealed class PowerHelperEngine : IDisposable
{
    private const int NativeRefreshRateFallback = 60;
    private const int ThrottledRefreshRateHertz = 60;

    // Slow enough to be invisible while the app sits in the tray, quick enough that a
    // percentage doesn't look frozen while someone is watching the window.
    public static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ActivePollInterval = TimeSpan.FromSeconds(5);

    private readonly SettingsService _settingsService = new();
    private readonly GpuDeviceService _gpuService = new();
    private readonly PowerMonitorService _powerMonitor = new();
    private readonly StartupService _startupService = new();
    private readonly BatteryStatusService _batteryService = new();
    private readonly PowerPlanService _powerPlanService = new();
    private readonly RefreshRateService _refreshRateService = new();
    private readonly BrightnessService _brightnessService = new();
    private readonly UpdateCheckService _updateCheckService = new();

    // Every device call and every status read passes through this. pnputil, powercfg and
    // the WMI queries are all slow enough to overlap otherwise, and BatteryStatusService
    // keeps rolling samples for its own charge-rate estimate that two concurrent readers
    // would corrupt.
    private readonly SemaphoreSlim _hardwareGate = new(1, 1);

    private readonly System.Threading.Timer _pollTimer;
    private readonly string? _gpuInstanceId;
    private readonly int _nativeRefreshRateHertz;

    private bool _lowBatteryWarningShown;
    private bool _disposed;

    // The "unlock" half of the lock/unlock brightness feature: the level captured right
    // before capping it for battery, so returning to AC restores exactly what the user had
    // rather than a guessed default. Null whenever nothing is currently capped.
    private int? _brightnessBeforeCap;

    public AppSettings Settings { get; }

    public bool GpuPresent => _gpuInstanceId is not null;

    public bool RefreshRateThrottleSupported { get; }

    public bool BrightnessSupported { get; }

    /// <summary>
    /// schtasks.exe is a process launch, so the answer is cached rather than re-queried.
    /// The previous code shelled out roughly once every three seconds for as long as the
    /// settings window stayed open, purely to redraw a switch that hadn't moved.
    /// </summary>
    public bool StartupRegistered { get; private set; }

    public UpdateInfo? AvailableUpdate { get; private set; }

    public StatusSnapshot LastStatus { get; private set; }

    public event Action<StatusSnapshot>? StatusChanged;

    /// <summary>Raised when a setting is changed through one surface so the other can redraw.</summary>
    public event Action? SettingsChanged;

    public event Action<BatteryStatus>? LowBatteryWarning;

    public event Action<UpdateInfo>? UpdateFound;

    public PowerHelperEngine()
    {
        Settings = _settingsService.Load();
        _gpuInstanceId = _gpuService.FindDiscreteGpuInstanceId();
        StartupRegistered = _startupService.IsRegistered();

        var supportedFrequencies = _refreshRateService.GetSupportedFrequencies();
        RefreshRateThrottleSupported = supportedFrequencies.Length > 1;
        _nativeRefreshRateHertz = supportedFrequencies.Length > 0
            ? supportedFrequencies[^1]
            : NativeRefreshRateFallback;

        BrightnessSupported = _brightnessService.GetBrightness() is not null;

        // Synchronous on purpose: the app must not present either surface before the
        // automatic policy has been applied once, or it would briefly report a state it
        // hasn't actually established yet.
        ApplyDesiredStateCore(_powerMonitor.IsOnBattery());
        LastStatus = ReadSnapshot();

        _powerMonitor.PowerSourceChanged += OnPowerSourceChanged;

        // AC/battery transitions trigger an immediate refresh via PowerSourceChanged, but
        // charge % and time-to-full/empty drift continuously in between, so poll too.
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
        // A skipped tick costs nothing here: another is always close behind, and waiting
        // would just queue reads against hardware that is already being read.
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
        var battery = _batteryService.GetStatus();
        var onBattery = _powerMonitor.IsOnBattery();
        var gpu = _gpuInstanceId is { } id ? _gpuService.GetState(id) : GpuState.NotFound;
        return new StatusSnapshot(battery, gpu, _gpuInstanceId is not null, onBattery);
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
    /// surface, and re-applies the automatic policy in the background so no UI thread is
    /// ever held while pnputil, powercfg or a WMI call runs.
    /// </summary>
    public void NotifySettingsChanged()
    {
        _settingsService.Save(Settings);
        SettingsChanged?.Invoke();
        _ = ApplyDesiredStateAsync();
    }

    public async Task<bool> SetStartupAsync(bool enabled)
    {
        var succeeded = await Task.Run(() => enabled ? _startupService.Register() : _startupService.Unregister())
            .ConfigureAwait(false);

        // Re-query rather than trust the action's own return value, so both surfaces
        // reflect what schtasks actually did, not what we asked it to do.
        StartupRegistered = await Task.Run(_startupService.IsRegistered).ConfigureAwait(false);
        SettingsChanged?.Invoke();
        return succeeded && StartupRegistered == enabled;
    }

    // ---------------------------------------------------------------- gpu

    /// <summary>
    /// A direct, one-off override of whatever the automatic policy last set - useful when
    /// the dGPU is needed right now despite being on battery. It is not sticky: the next
    /// AC/battery transition re-applies the automatic policy over this choice.
    /// </summary>
    public async Task<GpuActionResult> ToggleGpuManuallyAsync()
    {
        if (_gpuInstanceId is not { } instanceId)
        {
            return GpuActionResult.Unsupported;
        }

        // pnputil takes long enough on a real device that a second request arriving mid-run
        // would race the first and leave the adapter in whichever state finished last.
        if (!await _hardwareGate.WaitAsync(0).ConfigureAwait(false))
        {
            return GpuActionResult.Busy;
        }

        try
        {
            var succeeded = await Task.Run(() =>
            {
                var currentState = _gpuService.GetState(instanceId);
                return currentState == GpuState.Disabled
                    ? _gpuService.Enable(instanceId)
                    : _gpuService.Disable(instanceId);
            }).ConfigureAwait(false);

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
    /// Re-applies the automatic policy without blocking the caller. A request that arrives
    /// while one is running is dropped rather than queued, because the run in flight reads
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
            var onBattery = _powerMonitor.IsOnBattery();
            await Task.Run(() => ApplyDesiredStateCore(onBattery)).ConfigureAwait(false);
            PublishSnapshot(await Task.Run(ReadSnapshot).ConfigureAwait(false));
        }
        catch (Exception)
        {
            // Individual services already report failure through their return values; a
            // throw here means something unexpected, and the next transition will retry.
        }
        finally
        {
            _hardwareGate.Release();
        }
    }

    private void ApplyDesiredStateCore(bool onBattery)
    {
        ApplyGpuState(onBattery);
        ApplyPowerPlan(onBattery);
        ApplyRefreshRate(onBattery);
        ApplyBrightness(onBattery);
    }

    private void ApplyGpuState(bool onBattery)
    {
        if (_gpuInstanceId is null)
        {
            return;
        }

        var shouldDisable = Settings.AutoDisableDgpuOnBattery && onBattery;
        var currentState = _gpuService.GetState(_gpuInstanceId);

        if (shouldDisable && currentState != GpuState.Disabled)
        {
            _gpuService.Disable(_gpuInstanceId);
        }
        else if (!shouldDisable && currentState == GpuState.Disabled)
        {
            _gpuService.Enable(_gpuInstanceId);
        }
    }

    private void ApplyPowerPlan(bool onBattery)
    {
        if (!Settings.AutoSwitchPowerPlanOnBattery)
        {
            return;
        }

        if (onBattery)
        {
            _powerPlanService.SetPowerSaver();
        }
        else
        {
            // Balanced, not High performance: forcing High performance keeps the CPU's
            // minimum clock state at 100% even at idle, which ramps the fan from sustained
            // clock speed rather than actual heat - audibly noisy for no thermal reason.
            // Performance is still one Fn+Q or Windows Settings click away when needed.
            _powerPlanService.SetBalanced();
        }
    }

    private void ApplyRefreshRate(bool onBattery)
    {
        if (!Settings.ThrottleRefreshRateOnBattery || !RefreshRateThrottleSupported)
        {
            return;
        }

        var target = onBattery ? ThrottledRefreshRateHertz : _nativeRefreshRateHertz;
        _refreshRateService.TrySetFrequency(target);
    }

    private void ApplyBrightness(bool onBattery)
    {
        if (!BrightnessSupported)
        {
            return;
        }

        if (Settings.CapBrightnessOnBattery && onBattery)
        {
            // Lock: remember whatever the level was right before capping, but only on the
            // transition into a capped state - re-running this while already capped (e.g.
            // a periodic re-apply) must not overwrite the saved level with the capped value.
            _brightnessBeforeCap ??= _brightnessService.GetBrightness();

            _brightnessService.SetBrightness(Settings.BatteryBrightnessPercent);
        }
        else if (_brightnessBeforeCap is { } saved)
        {
            // Unlock: restore exactly what the user had, then clear so the next cap starts
            // from a fresh capture instead of restoring a now-stale remembered value.
            _brightnessService.SetBrightness(saved);
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
    /// Leaving the GPU disabled, the refresh rate throttled, or a battery-saver plan active
    /// with no running process to undo it would strand the user in that state, so a clean
    /// exit always restores.
    /// </summary>
    public void RestoreOnExit()
    {
        if (_gpuInstanceId is not null && _gpuService.GetState(_gpuInstanceId) == GpuState.Disabled)
        {
            _gpuService.Enable(_gpuInstanceId);
        }

        if (Settings.ThrottleRefreshRateOnBattery && RefreshRateThrottleSupported)
        {
            _refreshRateService.TrySetFrequency(_nativeRefreshRateHertz);
        }

        if (Settings.AutoSwitchPowerPlanOnBattery)
        {
            _powerPlanService.SetBalanced();
        }

        if (_brightnessBeforeCap is { } savedBrightness)
        {
            _brightnessService.SetBrightness(savedBrightness);
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
        _powerMonitor.PowerSourceChanged -= OnPowerSourceChanged;
        _powerMonitor.Dispose();
        _hardwareGate.Dispose();
    }
}
