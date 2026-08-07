using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

    // Target width a card should have room to breathe at; the actual per-card width is
    // computed from however many of these fit across the current window, so cards shrink
    // fluidly within a column count and only add a new column past a size threshold -
    // native WrapPanel wrapping, no fixed breakpoints.
    private const double IdealCardWidth = 380;
    private const int MaxColumns = 4;
    private const double CardGutter = 14;

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

    // The 5 cards, captured once from their XAML declaration order and then reparented into
    // freshly-built centered row containers on every layout pass (see UpdateCardLayout).
    private readonly List<FrameworkElement> _cards = [];

    public SettingsWindow(TrayApplicationContext context)
    {
        _context = context;
        InitializeComponent();

        _cards.AddRange(CardsPanel.Children.OfType<FrameworkElement>());
        CardsPanel.Children.Clear();

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

        if (!_context.BrightnessSupported)
        {
            BrightnessToggle.IsEnabled = false;
            BrightnessSlider.IsEnabled = false;
            BrightnessSubLabel.Text = "Not available - this display doesn't expose WMI brightness control.";
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null ? "dev build" : $"v{version.Major}.{version.Minor}.{version.Build}";

        LoadFromSettings();
        RefreshLiveStatus();
        SyncUpdateAvailability();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _refreshTimer.Tick += (_, _) =>
        {
            RefreshLiveStatus();
            LoadFromSettings();
            SyncUpdateAvailability();
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
        BrightnessToggle.IsChecked = settings.CapBrightnessOnBattery;
        BrightnessSlider.Value = settings.BatteryBrightnessPercent;
        BrightnessValueText.Text = $"{settings.BatteryBrightnessPercent}%";
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

    private string? _updateReleaseUrl;

    private void SyncUpdateAvailability()
    {
        var update = _context.AvailableUpdate;
        var visibility = update is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateAvailableSeparator.Visibility = visibility;
        UpdateAvailableLink.Visibility = visibility;

        if (update is { } found)
        {
            UpdateAvailableLink.Text = $"Update available: v{found.LatestVersion}";
            _updateReleaseUrl = found.ReleaseUrl;
        }
    }

    private void OnUpdateLinkClick(object sender, MouseButtonEventArgs e)
    {
        if (_updateReleaseUrl is { } url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        // The Slider's initial ValueChanged (fired during InitializeComponent, see
        // _initialized above) leaves keyboard focus on it, which WPF auto-scrolls into
        // view - landing mid-page instead of at the top the window should open on.
        RootScroll.ScrollToHome();
    }

    private void OnCardsPanelSizeChanged(object sender, SizeChangedEventArgs e) => UpdateCardLayout(e.NewSize.Width);

    private void UpdateCardLayout(double available)
    {
        if (available <= 0 || _cards.Count == 0)
        {
            return;
        }

        var columns = Math.Clamp((int)(available / IdealCardWidth), 1, MaxColumns);
        var cardWidth = (available - (CardGutter * (columns - 1))) / columns;

        foreach (var card in _cards)
        {
            card.Width = cardWidth;

            // CardsPanel.Children.Clear() below only detaches the row containers, not the
            // cards inside them - a card left parented to last call's (now-orphaned) row
            // can't be added to a new one. WPF fires SizeChanged more than once during
            // initial layout, so without this the second call throws immediately.
            if (card.Parent is System.Windows.Controls.Panel oldParent)
            {
                oldParent.Children.Remove(card);
            }
        }

        // Rebuild rows from scratch each time rather than trying to patch the existing
        // ones - regrouping 5 items is cheap, and this is the simplest way to guarantee an
        // incomplete last row (e.g. 2 cards left over at 3 columns) ends up centered instead
        // of left-aligned with a trailing gap, which is what made the grid look unfinished.
        CardsPanel.Children.Clear();
        for (var i = 0; i < _cards.Count; i += columns)
        {
            var row = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            };

            var rowCount = Math.Min(columns, _cards.Count - i);
            for (var j = 0; j < rowCount; j++)
            {
                row.Children.Add(_cards[i + j]);
            }

            CardsPanel.Children.Add(row);
        }
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

    private void OnBrightnessEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _context.Settings.CapBrightnessOnBattery = BrightnessToggle.IsChecked == true;
        _context.OnSettingsChangedExternally();
    }

    private void OnBrightnessLevelChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized)
        {
            return;
        }

        var percent = (int)e.NewValue;
        BrightnessValueText.Text = $"{percent}%";

        if (_suppressEvents)
        {
            return;
        }

        _context.Settings.BatteryBrightnessPercent = percent;
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
