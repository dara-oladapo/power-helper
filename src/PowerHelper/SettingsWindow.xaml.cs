using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PowerHelper.Services;

namespace PowerHelper;

public partial class SettingsWindow : Window
{
    private const string RepoUrl = "https://github.com/dara-oladapo/power-helper";

    private readonly TrayApplicationContext _context;
    private readonly DispatcherTimer _refreshTimer;

    // Guards against the Checked/Unchecked handlers re-entering while we're populating
    // control state programmatically (from the constructor or the periodic refresh),
    // which would otherwise re-save/re-apply settings that didn't actually change.
    private bool _suppressEvents;

    // Setting the Slider's Minimum in XAML forces a ValueChanged event during
    // InitializeComponent() itself - before later-declared named elements (like the
    // TextBlock the handler updates) have been constructed. _suppressEvents alone doesn't
    // cover this because it defaults false; this flag is checked first, before touching
    // any named element, and only becomes true once the constructor fully completes.
    private bool _initialized;

    public SettingsWindow(TrayApplicationContext context)
    {
        _context = context;
        InitializeComponent();

        if (_context.GpuInstanceId is null)
        {
            AutoDisableGpuToggle.IsEnabled = false;
            ManualGpuButton.IsEnabled = false;
        }

        if (!_context.RefreshRateThrottleSupported)
        {
            RefreshRateToggle.IsEnabled = false;
            RefreshRateSubLabel.Text = "Not available - your display only reports one refresh rate.";
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null ? "dev build" : $"v{version.Major}.{version.Minor}.{version.Build}";

        LoadFromSettings();
        RefreshLiveStatus();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _refreshTimer.Tick += (_, _) =>
        {
            RefreshLiveStatus();
            LoadFromSettings();
        };
        _refreshTimer.Start();

        Closed += (_, _) => _refreshTimer.Stop();
        _initialized = true;
    }

    private void LoadFromSettings()
    {
        _suppressEvents = true;

        var settings = _context.Settings;
        AutoDisableGpuToggle.IsChecked = settings.AutoDisableDgpuOnBattery;
        PowerPlanToggle.IsChecked = settings.AutoSwitchPowerPlanOnBattery;
        RefreshRateToggle.IsChecked = settings.ThrottleRefreshRateOnBattery;
        LowBatteryToggle.IsChecked = settings.LowBatteryWarningEnabled;
        ThresholdSlider.Value = settings.LowBatteryWarningThresholdPercent;
        ThresholdValueText.Text = $"{settings.LowBatteryWarningThresholdPercent}%";
        StartupToggle.IsChecked = _context.StartupService.IsRegistered();

        _suppressEvents = false;
    }

    private void RefreshLiveStatus()
    {
        var amber = (System.Windows.Media.Brush)FindResource("AmberBrush");
        var dim = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0x47, 0x3F));

        var battery = _context.BatteryService.GetStatus();
        BatteryStatusText.Text = (battery.BatteryPresent ? $"BATTERY: {battery.Description}" : battery.Description).ToUpperInvariant();
        BatteryLed.Fill = battery.Charging ? amber : dim;

        if (_context.GpuInstanceId is null)
        {
            GpuStatusText.Text = "NO DISCRETE GPU DETECTED";
            GpuLed.Fill = dim;
            return;
        }

        var state = _context.GpuService.GetState(_context.GpuInstanceId);
        var onBattery = _context.PowerMonitor.IsOnBattery();
        var power = onBattery ? "on battery" : "on AC power";
        GpuStatusText.Text = $"DGPU: {state} ({power})".ToUpperInvariant();
        GpuLed.Fill = state == GpuState.Disabled ? dim : amber;
        ManualGpuButton.Content = state == GpuState.Disabled ? "ENABLE DGPU NOW" : "DISABLE DGPU NOW";
    }

    private void OnSliderPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // WPF's Slider changes its own Value on mouse wheel once it has focus (e.g. after
        // you click/drag it), which silently hijacks scrolling for the rest of the page.
        // Swallow it here and forward the same wheel motion to the outer ScrollViewer
        // instead, so the wheel always scrolls and the slider only moves via drag/arrows.
        e.Handled = true;
        var forwarded = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = sender,
        };
        RootScroll.RaiseEvent(forwarded);
    }

    private void OnGitHubLinkClick(object sender, MouseButtonEventArgs e)
    {
        Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true });
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        // The Slider's initial ValueChanged (fired during InitializeComponent, see
        // _initialized above) leaves keyboard focus on it, which WPF auto-scrolls into
        // view - landing mid-page instead of at the top the window should open on.
        RootScroll.ScrollToHome();
    }

    private void OnManualGpuButtonClick(object sender, RoutedEventArgs e)
    {
        _context.ToggleGpuManually();
        RefreshLiveStatus();
    }

    private void OnAutoDisableGpuChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _context.Settings.AutoDisableDgpuOnBattery = AutoDisableGpuToggle.IsChecked == true;
        _context.OnSettingsChangedExternally();
        RefreshLiveStatus();
    }

    private void OnPowerPlanChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _context.Settings.AutoSwitchPowerPlanOnBattery = PowerPlanToggle.IsChecked == true;
        _context.OnSettingsChangedExternally();
    }

    private void OnRefreshRateChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _context.Settings.ThrottleRefreshRateOnBattery = RefreshRateToggle.IsChecked == true;
        _context.OnSettingsChangedExternally();
    }

    private void OnLowBatteryEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _context.Settings.LowBatteryWarningEnabled = LowBatteryToggle.IsChecked == true;
        _context.OnSettingsChangedExternally();
    }

    private void OnThresholdChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized)
        {
            return;
        }

        var percent = (int)e.NewValue;
        ThresholdValueText.Text = $"{percent}%";

        if (_suppressEvents)
        {
            return;
        }

        _context.Settings.LowBatteryWarningThresholdPercent = percent;
        _context.OnSettingsChangedExternally();
    }

    private void OnStartupChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        var wantEnabled = StartupToggle.IsChecked == true;
        if (wantEnabled)
        {
            _context.StartupService.Register();
        }
        else
        {
            _context.StartupService.Unregister();
        }

        // Re-query rather than trust the action's own return value, so the toggle always
        // reflects what schtasks actually did, not what we asked it to do.
        _suppressEvents = true;
        StartupToggle.IsChecked = _context.StartupService.IsRegistered();
        _suppressEvents = false;
    }
}
