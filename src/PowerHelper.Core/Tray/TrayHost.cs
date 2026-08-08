using System.Drawing;
using System.Windows.Forms;
using PowerHelper.Core;
using PowerHelper.Services;

namespace PowerHelper.Tray;

/// <summary>
/// The notification-area icon and its menu, running on a dedicated STA thread with its own
/// WinForms message loop.
///
/// It needs its own loop because the app's main thread belongs to WinUI: a
/// <see cref="NotifyIcon"/> and a <see cref="ContextMenuStrip"/> both depend on WinForms'
/// own message filtering for click routing and menu dismissal, which only exists inside
/// <see cref="Application.Run(ApplicationContext)"/>. Sharing the WinUI dispatcher would
/// leave the menu unable to close itself on an outside click.
///
/// Everything crossing into this thread goes through <see cref="Post"/>.
/// </summary>
public sealed class TrayHost : IDisposable
{
    private readonly PowerHelperEngine _engine;

    private Thread? _thread;
    private ApplicationContext? _context;
    private Control? _marshal;
    private NotifyIcon? _trayIcon;
    private ContextMenuStrip? _menu;
    private TrayIconRenderer? _iconRenderer;

    private ToolStripLabel _batteryLabel = null!;
    private ToolStripLabel _gpuLabel = null!;
    private ToolStripMenuItem _manualGpuItem = null!;
    private ToolStripMenuItem _automaticItem = null!;
    private ToolStripMenuItem _autoDisableItem = null!;
    private ToolStripMenuItem _powerPlanItem = null!;
    private ToolStripMenuItem _refreshRateItem = null!;
    private ToolStripMenuItem _brightnessItem = null!;
    private ToolStripMenuItem _lowBatteryItem = null!;
    private ToolStripMenuItem _startupItem = null!;
    private ToolStripMenuItem _updateItem = null!;

    private bool _disposed;

    public event Action? OpenSettingsRequested;

    public event Action? ExitRequested;

    public event Action? CheckForUpdatesRequested;

    public TrayHost(PowerHelperEngine engine) => _engine = engine;

    /// <summary>Blocks until the icon is actually visible, so startup order is deterministic.</summary>
    public void Start()
    {
        using var ready = new ManualResetEventSlim(false);

        _thread = new Thread(() => Run(ready))
        {
            IsBackground = true,
            Name = "PowerHelper.Tray",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        ready.Wait();

        _engine.StatusChanged += OnEngineStatusChanged;
        _engine.SettingsChanged += OnEngineSettingsChanged;
        _engine.LowBatteryWarning += OnLowBatteryWarning;
        _engine.UpdateFound += OnUpdateFound;
    }

    private void Run(ManualResetEventSlim ready)
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _marshal = new Control();
            _marshal.CreateControl();

            BuildMenu();

            _trayIcon = new NotifyIcon
            {
                // Extracted from the exe's own embedded icon rather than a separate
                // resource, so there is a single source of truth. This is the fallback
                // shown before the first battery render, and permanently on a machine with
                // no battery (TrayIconRenderer only replaces it when one exists).
                Icon = LoadApplicationIcon(),
                ContextMenuStrip = _menu,
                Visible = true,
                Text = "Power Helper",
            };

            // Every other tray icon on the desktop - network, volume, OneDrive - opens its
            // surface on a single left click. Double-click alone meant the most obvious
            // thing to try did nothing at all.
            _trayIcon.MouseClick += OnTrayIconMouseClick;

            _iconRenderer = new TrayIconRenderer();

            ApplyTheme();
            Render(_engine.LastStatus);
            SyncChecks();

            _context = new ApplicationContext();
        }
        finally
        {
            // Set even on failure, so a tray that couldn't start doesn't hang the caller
            // waiting for it. The app still runs; it just has no icon.
            ready.Set();
        }

        // Null only if the setup above threw. Running a null context would turn a failed
        // tray into an unhandled exception on a background thread, which takes the whole
        // process down - a worse outcome than a missing icon.
        if (_context is not null)
        {
            Application.Run(_context);
        }
    }

    private static Icon LoadApplicationIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path) && Icon.ExtractAssociatedIcon(path) is { } icon)
            {
                return icon;
            }
        }
        catch (Exception)
        {
            // Falls through to the stock icon below.
        }

        return SystemIcons.Application;
    }

    private void BuildMenu()
    {
        _batteryLabel = new ToolStripLabel("Battery: checking…");
        _gpuLabel = new ToolStripLabel(_engine.GpuPresent ? "Discrete GPU: checking…" : "No discrete GPU detected");

        _manualGpuItem = new ToolStripMenuItem("Enable dGPU now", null, OnManualGpuClicked)
        {
            Enabled = _engine.GpuPresent,
        };

        _autoDisableItem = new ToolStripMenuItem("Disable discrete GPU", null, OnToggleAutoDisable)
        {
            Enabled = _engine.GpuPresent,
        };
        _powerPlanItem = new ToolStripMenuItem("Switch to the Power saver plan", null, OnTogglePowerPlan);
        _refreshRateItem = new ToolStripMenuItem("Drop to 60 Hz", null, OnToggleRefreshRate)
        {
            Enabled = _engine.RefreshRateThrottleSupported,
        };
        _brightnessItem = new ToolStripMenuItem("Lock brightness", null, OnToggleBrightness)
        {
            Enabled = _engine.BrightnessSupported,
        };

        // Four policies that all answer the same question - "what should happen when I
        // unplug?" - so they read better as one named group than as four sibling lines
        // interleaved with unrelated commands, which is what the top level used to be.
        _automaticItem = new ToolStripMenuItem("Automatically on battery");
        _automaticItem.DropDownItems.AddRange(
        [
            _autoDisableItem,
            _powerPlanItem,
            _refreshRateItem,
            _brightnessItem,
        ]);

        _lowBatteryItem = new ToolStripMenuItem("Warn at low battery", null, OnToggleLowBatteryWarning);
        _startupItem = new ToolStripMenuItem("Start with Windows", null, OnToggleStartup);
        _updateItem = new ToolStripMenuItem("Check for updates…", null, (_, _) => CheckForUpdatesRequested?.Invoke());

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_batteryLabel);
        _menu.Items.Add(_gpuLabel);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_manualGpuItem);
        _menu.Items.Add(_automaticItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_lowBatteryItem);
        _menu.Items.Add(_startupItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Settings…", null, (_, _) => OpenSettingsRequested?.Invoke());
        _menu.Items.Add(_updateItem);
        _menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());

        // Re-applied on open as well as on theme change: the menu's own handle is created
        // lazily, so the first rounded-corner request has nothing to act on.
        _menu.Opened += (_, _) => DwmRoundIfPossible();
    }

    private void DwmRoundIfPossible()
    {
        if (_menu is { IsHandleCreated: true } menu)
        {
            Interop.DwmWindowStyle.SetRoundedCorners(menu.Handle);
        }
    }

    // ---------------------------------------------------------------- theming

    public void ApplyTheme() => Post(() =>
    {
        if (_menu is { } menu)
        {
            TrayMenuTheme.Apply(menu, TrayPalette.For(SystemThemeService.CurrentTheme));
        }
    });

    // ---------------------------------------------------------------- rendering

    private void OnEngineStatusChanged(StatusSnapshot snapshot) => Post(() => Render(snapshot));

    private void OnEngineSettingsChanged() => Post(SyncChecks);

    private void Render(StatusSnapshot snapshot)
    {
        if (_trayIcon is null)
        {
            return;
        }

        var battery = snapshot.Battery;

        _batteryLabel.Text = battery.BatteryPresent
            ? $"Battery: {battery.Description}"
            : battery.Description;

        if (!snapshot.GpuPresent)
        {
            _gpuLabel.Text = "No discrete GPU detected";
        }
        else
        {
            var power = snapshot.OnBattery ? "on battery" : "on AC power";
            _gpuLabel.Text = $"Discrete GPU: {(snapshot.GpuEnabled ? "on" : "off")} ({power})";
            _manualGpuItem.Text = snapshot.GpuEnabled ? "Disable dGPU now" : "Enable dGPU now";
        }

        var tooltip = $"{battery.Description}\n{_gpuLabel.Text}";
        // NotifyIcon.Text is capped at 127 characters; truncate defensively rather than throw.
        _trayIcon.Text = tooltip.Length <= 127 ? tooltip : tooltip[..127];

        if (battery.BatteryPresent && _iconRenderer is { } renderer)
        {
            renderer.Apply(_trayIcon, FormatIconText(battery));
        }
    }

    private static string FormatIconText(BatteryStatus battery)
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

    private void SyncChecks()
    {
        var settings = _engine.Settings;

        _autoDisableItem.Checked = settings.AutoDisableDgpuOnBattery;
        _powerPlanItem.Checked = settings.AutoSwitchPowerPlanOnBattery;
        _refreshRateItem.Checked = settings.ThrottleRefreshRateOnBattery;
        _brightnessItem.Checked = settings.CapBrightnessOnBattery;
        _brightnessItem.Text = $"Lock brightness to {settings.BatteryBrightnessPercent}%";
        _lowBatteryItem.Checked = settings.LowBatteryWarningEnabled;
        _lowBatteryItem.Text = $"Warn at {settings.LowBatteryWarningThresholdPercent}% battery";
        _startupItem.Checked = _engine.StartupRegistered;

        _updateItem.Text = _engine.AvailableUpdate is { } update
            ? $"Update available: v{update.LatestVersion}"
            : "Check for updates…";
    }

    // ---------------------------------------------------------------- notifications

    private void OnLowBatteryWarning(BatteryStatus battery) => Post(() =>
        _trayIcon?.ShowBalloonTip(10_000, "Low battery", battery.Description, ToolTipIcon.Warning));

    private void OnUpdateFound(UpdateInfo update) => Post(() =>
    {
        SyncChecks();
        _trayIcon?.ShowBalloonTip(
            10_000,
            "Update available",
            $"Power Helper v{update.LatestVersion} is ready to download.",
            ToolTipIcon.Info);
    });

    public void ShowBalloon(string title, string message) => Post(() =>
        _trayIcon?.ShowBalloonTip(5_000, title, message, ToolTipIcon.Info));

    // ---------------------------------------------------------------- commands

    private void OnTrayIconMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            OpenSettingsRequested?.Invoke();
        }
    }

    private async void OnManualGpuClicked(object? sender, EventArgs e)
    {
        _manualGpuItem.Enabled = false;
        try
        {
            await _engine.ToggleGpuManuallyAsync();
        }
        finally
        {
            _manualGpuItem.Enabled = _engine.GpuPresent;
        }
    }

    private void OnToggleAutoDisable(object? sender, EventArgs e)
    {
        _engine.Settings.AutoDisableDgpuOnBattery = !_engine.Settings.AutoDisableDgpuOnBattery;
        _engine.NotifySettingsChanged();
    }

    private void OnTogglePowerPlan(object? sender, EventArgs e)
    {
        _engine.Settings.AutoSwitchPowerPlanOnBattery = !_engine.Settings.AutoSwitchPowerPlanOnBattery;
        _engine.NotifySettingsChanged();
    }

    private void OnToggleRefreshRate(object? sender, EventArgs e)
    {
        _engine.Settings.ThrottleRefreshRateOnBattery = !_engine.Settings.ThrottleRefreshRateOnBattery;
        _engine.NotifySettingsChanged();
    }

    private void OnToggleBrightness(object? sender, EventArgs e)
    {
        _engine.Settings.CapBrightnessOnBattery = !_engine.Settings.CapBrightnessOnBattery;
        _engine.NotifySettingsChanged();
    }

    private void OnToggleLowBatteryWarning(object? sender, EventArgs e)
    {
        _engine.Settings.LowBatteryWarningEnabled = !_engine.Settings.LowBatteryWarningEnabled;
        _engine.NotifySettingsChanged();
    }

    private async void OnToggleStartup(object? sender, EventArgs e)
    {
        _startupItem.Enabled = false;
        try
        {
            await _engine.SetStartupAsync(!_engine.StartupRegistered);
        }
        finally
        {
            _startupItem.Enabled = true;
        }
    }

    // ---------------------------------------------------------------- plumbing

    private void Post(Action action)
    {
        if (_marshal is not { IsDisposed: false } marshal || !marshal.IsHandleCreated)
        {
            return;
        }

        try
        {
            if (marshal.InvokeRequired)
            {
                marshal.BeginInvoke(action);
            }
            else
            {
                action();
            }
        }
        catch (ObjectDisposedException)
        {
            // The tray thread went away between the check and the post - nothing to update.
        }
        catch (InvalidOperationException)
        {
            // Same race, reported differently when the handle is destroyed mid-call.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _engine.StatusChanged -= OnEngineStatusChanged;
        _engine.SettingsChanged -= OnEngineSettingsChanged;
        _engine.LowBatteryWarning -= OnLowBatteryWarning;
        _engine.UpdateFound -= OnUpdateFound;

        Post(() =>
        {
            if (_trayIcon is { } icon)
            {
                // Hidden before disposal: an icon disposed while still visible can leave a
                // ghost in the notification area until the user hovers over it.
                icon.Visible = false;
                icon.Dispose();
            }

            _menu?.Dispose();
            _context?.ExitThread();
        });

        _thread?.Join(TimeSpan.FromSeconds(2));
    }
}
