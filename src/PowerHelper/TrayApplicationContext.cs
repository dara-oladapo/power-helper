using System.Diagnostics;
using System.Reflection;
using PowerHelper.Models;
using PowerHelper.Services;

namespace PowerHelper;

public sealed class TrayApplicationContext : ApplicationContext
{
    private const int NativeRefreshRateFallback = 60;
    private const int ThrottledRefreshRateHertz = 60;

    private readonly SettingsService _settingsService = new();
    private readonly GpuDeviceService _gpuService = new();
    private readonly PowerMonitorService _powerMonitor = new();
    private readonly StartupService _startupService = new();
    private readonly BatteryStatusService _batteryService = new();
    private readonly PowerPlanService _powerPlanService = new();
    private readonly RefreshRateService _refreshRateService = new();
    private readonly BrightnessService _brightnessService = new();
    private readonly UpdateCheckService _updateCheckService = new();
    private readonly TrayIconRenderer _iconRenderer = new();

    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _batteryStatusItem;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _manualGpuToggleItem;
    private readonly ToolStripMenuItem _autoDisableItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _powerPlanItem;
    private readonly ToolStripMenuItem _refreshRateItem;
    private readonly ToolStripMenuItem _lowBatteryWarningItem;
    private readonly ToolStripMenuItem _brightnessItem;
    private readonly ToolStripMenuItem _updateItem;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    private readonly AppSettings _settings;
    private readonly string? _gpuInstanceId;
    private readonly bool _refreshRateThrottleSupported;
    private readonly int _nativeRefreshRateHertz;
    private readonly bool _brightnessSupported;

    private bool _lowBatteryWarningShown;
    private SettingsWindow? _settingsWindow;

    // The "unlock" half of the lock/unlock brightness feature: the level captured right
    // before capping it for battery, so returning to AC restores exactly what the user had
    // rather than a guessed default. Null whenever nothing is currently capped.
    private int? _brightnessBeforeCap;
    private UpdateInfo? _availableUpdate;

    internal AppSettings Settings => _settings;
    internal string? GpuInstanceId => _gpuInstanceId;
    internal bool RefreshRateThrottleSupported => _refreshRateThrottleSupported;
    internal bool BrightnessSupported => _brightnessSupported;
    internal UpdateInfo? AvailableUpdate => _availableUpdate;
    internal GpuDeviceService GpuService => _gpuService;
    internal BatteryStatusService BatteryService => _batteryService;
    internal PowerMonitorService PowerMonitor => _powerMonitor;
    internal RefreshRateService RefreshRateService => _refreshRateService;
    internal StartupService StartupService => _startupService;

    public TrayApplicationContext()
    {
        _settings = _settingsService.Load();
        _gpuInstanceId = _gpuService.FindDiscreteGpuInstanceId();

        var supportedFrequencies = _refreshRateService.GetSupportedFrequencies();
        _refreshRateThrottleSupported = supportedFrequencies.Length > 1;
        _nativeRefreshRateHertz = supportedFrequencies.Length > 0
            ? supportedFrequencies[^1]
            : NativeRefreshRateFallback;

        _brightnessSupported = _brightnessService.GetBrightness() is not null;

        _batteryStatusItem = new ToolStripMenuItem("Battery: checking...") { Enabled = false };
        _statusItem = new ToolStripMenuItem("Status: checking...") { Enabled = false };
        _manualGpuToggleItem = new ToolStripMenuItem("Enable dGPU now", null, OnManualGpuToggle)
        {
            Enabled = _gpuInstanceId is not null,
        };
        _autoDisableItem = new ToolStripMenuItem(
            "Disable dGPU on battery", null, OnToggleAutoDisable)
        {
            Checked = _settings.AutoDisableDgpuOnBattery,
        };
        _powerPlanItem = new ToolStripMenuItem(
            "Power saver on battery / Balanced on AC", null, OnTogglePowerPlan)
        {
            Checked = _settings.AutoSwitchPowerPlanOnBattery,
        };
        _refreshRateItem = new ToolStripMenuItem(
            $"Drop to {ThrottledRefreshRateHertz}Hz on battery", null, OnToggleRefreshRateThrottle)
        {
            Checked = _settings.ThrottleRefreshRateOnBattery,
            Enabled = _refreshRateThrottleSupported,
        };
        _lowBatteryWarningItem = new ToolStripMenuItem(
            $"Warn at {_settings.LowBatteryWarningThresholdPercent}% battery", null, OnToggleLowBatteryWarning)
        {
            Checked = _settings.LowBatteryWarningEnabled,
        };
        _brightnessItem = new ToolStripMenuItem(
            $"Lock brightness to {_settings.BatteryBrightnessPercent}% on battery", null, OnToggleBrightnessCap)
        {
            Checked = _settings.CapBrightnessOnBattery,
            Enabled = _brightnessSupported,
        };
        _startupItem = new ToolStripMenuItem("Start with Windows", null, OnToggleStartup)
        {
            Checked = _startupService.IsRegistered(),
        };
        _updateItem = new ToolStripMenuItem("Check for Updates...", null, OnCheckForUpdatesClicked);

        var menu = new ContextMenuStrip();
        menu.Items.Add(_batteryStatusItem);
        menu.Items.Add(_statusItem);
        menu.Items.Add(_manualGpuToggleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autoDisableItem);
        menu.Items.Add(_powerPlanItem);
        menu.Items.Add(_refreshRateItem);
        menu.Items.Add(_brightnessItem);
        menu.Items.Add(_lowBatteryWarningItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings...", null, OnOpenSettings);
        menu.Items.Add(_updateItem);
        menu.Items.Add("Exit", null, OnExit);

        _trayIcon = new NotifyIcon
        {
            // Extracted from the exe's own embedded ApplicationIcon rather than a separate
            // resource, so there's a single source of truth for the app's icon. This is the
            // fallback shown before the first battery-status render, and permanently on a
            // desktop with no battery (TrayIconRenderer only ever replaces it when one exists).
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true,
            Text = "Power Helper",
        };
        _trayIcon.DoubleClick += OnOpenSettings;

        if (_gpuInstanceId is null)
        {
            _statusItem.Text = "No discrete GPU detected";
            _autoDisableItem.Enabled = false;
        }

        ApplyDesiredState();
        RefreshBatteryStatus();

        _powerMonitor.PowerSourceChanged += OnPowerSourceChanged;

        // AC/battery transitions trigger an immediate refresh via PowerSourceChanged, but
        // charge % and time-to-full/empty drift continuously in between, so poll too.
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 30_000 };
        _refreshTimer.Tick += (_, _) => RefreshBatteryStatus();
        _refreshTimer.Start();

        // Delayed and silent: fires once shortly after startup so it isn't competing with
        // the rest of init, and never bothers the user unless it actually finds something.
        var startupCheckTimer = new System.Windows.Forms.Timer { Interval = 5_000 };
        startupCheckTimer.Tick += async (_, _) =>
        {
            startupCheckTimer.Stop();
            startupCheckTimer.Dispose();
            await CheckForUpdatesAsync(announceIfNoneFound: false);
        };
        startupCheckTimer.Start();
    }

    private void OnPowerSourceChanged(bool onBattery)
    {
        ApplyDesiredState();
        RefreshBatteryStatus();
    }

    private void RefreshBatteryStatus()
    {
        var battery = _batteryService.GetStatus();
        _batteryStatusItem.Text = battery.BatteryPresent ? $"Battery: {battery.Description}" : battery.Description;
        UpdateLowBatteryWarning(battery);
        UpdateTrayTooltip(battery);

        if (battery.BatteryPresent)
        {
            _iconRenderer.Apply(_trayIcon, GetTrayIconText(battery));
        }
    }

    private static string GetTrayIconText(BatteryStatus battery)
    {
        if (battery.TimeRemaining is { } remaining)
        {
            TrayIconRenderer.FormatCompactDuration(remaining, out var text);
            return text;
        }

        // Time-remaining is still "calculating" (e.g. charge rate not yet sampled) - fall
        // back to percentage rather than showing nothing.
        return battery.PercentCharged.ToString();
    }

    private void UpdateLowBatteryWarning(BatteryStatus battery)
    {
        if (!_settings.LowBatteryWarningEnabled || !battery.BatteryPresent || battery.PluggedIn)
        {
            _lowBatteryWarningShown = false;
            return;
        }

        if (!_lowBatteryWarningShown && battery.PercentCharged <= _settings.LowBatteryWarningThresholdPercent)
        {
            _trayIcon.ShowBalloonTip(10_000, "Low battery", battery.Description, ToolTipIcon.Warning);
            _lowBatteryWarningShown = true;
        }
        // Hysteresis band above the threshold avoids re-alerting on every poll while
        // hovering right at the cutoff.
        else if (battery.PercentCharged > _settings.LowBatteryWarningThresholdPercent + 5)
        {
            _lowBatteryWarningShown = false;
        }
    }

    private void UpdateTrayTooltip(BatteryStatus battery)
    {
        var gpuLine = _gpuInstanceId is null
            ? "No discrete GPU detected"
            : _statusItem.Text;

        var tooltip = $"{battery.Description}\n{gpuLine}";
        // NotifyIcon.Text is capped at 127 characters; truncate defensively rather than throw.
        _trayIcon.Text = tooltip.Length <= 127 ? tooltip : tooltip[..127];
    }

    private void OnOpenSettings(object? sender, EventArgs e)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        else
        {
            _settingsWindow.Activate();
        }
    }

    private async void OnCheckForUpdatesClicked(object? sender, EventArgs e)
    {
        // Already know about one from the background startup check - no need to hit the
        // network again, just take them straight to it.
        if (_availableUpdate is { } known)
        {
            Process.Start(new ProcessStartInfo(known.ReleaseUrl) { UseShellExecute = true });
            return;
        }

        await CheckForUpdatesAsync(announceIfNoneFound: true);
    }

    private async Task CheckForUpdatesAsync(bool announceIfNoneFound)
    {
        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        var update = await _updateCheckService.CheckForUpdateAsync(currentVersion);

        if (update is { } found)
        {
            _availableUpdate = found;
            _updateItem.Text = $"Update available: v{found.LatestVersion}";
            _trayIcon.ShowBalloonTip(
                10_000,
                "Update available",
                $"Power Helper v{found.LatestVersion} is ready to download.",
                ToolTipIcon.Info);
        }
        else if (announceIfNoneFound)
        {
            _trayIcon.ShowBalloonTip(5_000, "Power Helper", "You're up to date.", ToolTipIcon.Info);
        }
    }

    /// <summary>
    /// Called by SettingsWindow after it mutates a field on the shared Settings instance
    /// directly - persists it, re-applies the automatic AC/battery policy with the new
    /// values, and keeps the tray context menu's checkmarks in sync so the two surfaces
    /// never disagree about current state.
    /// </summary>
    internal void OnSettingsChangedExternally()
    {
        _settingsService.Save(_settings);
        ApplyDesiredState();
        RefreshBatteryStatus();
        SyncMenuChecks();
    }

    private void SyncMenuChecks()
    {
        _autoDisableItem.Checked = _settings.AutoDisableDgpuOnBattery;
        _powerPlanItem.Checked = _settings.AutoSwitchPowerPlanOnBattery;
        _refreshRateItem.Checked = _settings.ThrottleRefreshRateOnBattery;
        _lowBatteryWarningItem.Checked = _settings.LowBatteryWarningEnabled;
        _lowBatteryWarningItem.Text = $"Warn at {_settings.LowBatteryWarningThresholdPercent}% battery";
        _brightnessItem.Checked = _settings.CapBrightnessOnBattery;
        _brightnessItem.Text = $"Lock brightness to {_settings.BatteryBrightnessPercent}% on battery";
        _startupItem.Checked = _startupService.IsRegistered();
    }

    private void OnManualGpuToggle(object? sender, EventArgs e) => ToggleGpuManually();

    /// <summary>
    /// A direct, one-off override of whatever the automatic AC/battery policy just set -
    /// useful when you need the dGPU right now despite being on battery. It's not sticky:
    /// the next AC/battery transition re-applies the automatic policy over this choice.
    /// Shared by the tray menu item and the settings window's manual toggle button.
    /// </summary>
    internal void ToggleGpuManually()
    {
        if (_gpuInstanceId is null)
        {
            return;
        }

        var currentState = _gpuService.GetState(_gpuInstanceId);
        if (currentState == GpuState.Disabled)
        {
            _gpuService.Enable(_gpuInstanceId);
        }
        else
        {
            _gpuService.Disable(_gpuInstanceId);
        }

        UpdateStatusText(_powerMonitor.IsOnBattery());
        RefreshBatteryStatus();
    }

    private void OnToggleAutoDisable(object? sender, EventArgs e)
    {
        _settings.AutoDisableDgpuOnBattery = !_settings.AutoDisableDgpuOnBattery;
        _autoDisableItem.Checked = _settings.AutoDisableDgpuOnBattery;
        _settingsService.Save(_settings);
        ApplyDesiredState();
    }

    private void OnTogglePowerPlan(object? sender, EventArgs e)
    {
        _settings.AutoSwitchPowerPlanOnBattery = !_settings.AutoSwitchPowerPlanOnBattery;
        _powerPlanItem.Checked = _settings.AutoSwitchPowerPlanOnBattery;
        _settingsService.Save(_settings);
        ApplyDesiredState();
    }

    private void OnToggleRefreshRateThrottle(object? sender, EventArgs e)
    {
        _settings.ThrottleRefreshRateOnBattery = !_settings.ThrottleRefreshRateOnBattery;
        _refreshRateItem.Checked = _settings.ThrottleRefreshRateOnBattery;
        _settingsService.Save(_settings);
        ApplyDesiredState();
    }

    private void OnToggleBrightnessCap(object? sender, EventArgs e)
    {
        _settings.CapBrightnessOnBattery = !_settings.CapBrightnessOnBattery;
        _brightnessItem.Checked = _settings.CapBrightnessOnBattery;
        _settingsService.Save(_settings);
        ApplyDesiredState();
    }

    private void OnToggleLowBatteryWarning(object? sender, EventArgs e)
    {
        _settings.LowBatteryWarningEnabled = !_settings.LowBatteryWarningEnabled;
        _lowBatteryWarningItem.Checked = _settings.LowBatteryWarningEnabled;
        _settingsService.Save(_settings);
    }

    private void OnToggleStartup(object? sender, EventArgs e)
    {
        if (_startupItem.Checked)
        {
            if (_startupService.Unregister())
            {
                _startupItem.Checked = false;
            }
        }
        else if (_startupService.Register())
        {
            _startupItem.Checked = true;
        }
    }

    private void ApplyDesiredState()
    {
        var onBattery = _powerMonitor.IsOnBattery();

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

        var shouldDisable = _settings.AutoDisableDgpuOnBattery && onBattery;
        var currentState = _gpuService.GetState(_gpuInstanceId);

        if (shouldDisable && currentState != GpuState.Disabled)
        {
            _gpuService.Disable(_gpuInstanceId);
        }
        else if (!shouldDisable && currentState == GpuState.Disabled)
        {
            _gpuService.Enable(_gpuInstanceId);
        }

        UpdateStatusText(onBattery);
    }

    private void ApplyPowerPlan(bool onBattery)
    {
        if (!_settings.AutoSwitchPowerPlanOnBattery)
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
            // Performance is still one Fn+Q or Windows Settings click away when actually needed.
            _powerPlanService.SetBalanced();
        }
    }

    private void ApplyRefreshRate(bool onBattery)
    {
        if (!_settings.ThrottleRefreshRateOnBattery || !_refreshRateThrottleSupported)
        {
            return;
        }

        var target = onBattery ? ThrottledRefreshRateHertz : _nativeRefreshRateHertz;
        _refreshRateService.TrySetFrequency(target);
    }

    private void ApplyBrightness(bool onBattery)
    {
        if (!_brightnessSupported)
        {
            return;
        }

        if (_settings.CapBrightnessOnBattery && onBattery)
        {
            // Lock: remember whatever the level was right before capping, but only on the
            // transition into a capped state - re-running this while already capped (e.g.
            // periodic re-apply) must not overwrite the saved level with the capped value.
            if (_brightnessBeforeCap is null)
            {
                _brightnessBeforeCap = _brightnessService.GetBrightness();
            }

            _brightnessService.SetBrightness(_settings.BatteryBrightnessPercent);
        }
        else if (_brightnessBeforeCap is { } saved)
        {
            // Unlock: restore exactly what the user had, then clear so the next cap starts
            // from a fresh capture instead of restoring a now-stale remembered value.
            _brightnessService.SetBrightness(saved);
            _brightnessBeforeCap = null;
        }
    }

    private void UpdateStatusText(bool onBattery)
    {
        if (_gpuInstanceId is null)
        {
            return;
        }

        var state = _gpuService.GetState(_gpuInstanceId);
        var power = onBattery ? "on battery" : "on AC power";
        _statusItem.Text = $"dGPU: {state} ({power})";
        _manualGpuToggleItem.Text = state == GpuState.Disabled ? "Enable dGPU now" : "Disable dGPU now";
    }

    private void OnExit(object? sender, EventArgs e)
    {
        // Leaving the GPU disabled, refresh rate throttled, or a battery-saver plan active
        // with no running process to undo it would strand the user in that state, so always
        // restore on a clean exit.
        if (_gpuInstanceId is not null && _gpuService.GetState(_gpuInstanceId) == GpuState.Disabled)
        {
            _gpuService.Enable(_gpuInstanceId);
        }

        if (_settings.ThrottleRefreshRateOnBattery && _refreshRateThrottleSupported)
        {
            _refreshRateService.TrySetFrequency(_nativeRefreshRateHertz);
        }

        if (_settings.AutoSwitchPowerPlanOnBattery)
        {
            _powerPlanService.SetBalanced();
        }

        if (_brightnessBeforeCap is { } savedBrightness)
        {
            _brightnessService.SetBrightness(savedBrightness);
        }

        _refreshTimer.Stop();
        _refreshTimer.Dispose();
        _trayIcon.Visible = false;
        _powerMonitor.Dispose();
        Application.Exit();
    }
}
