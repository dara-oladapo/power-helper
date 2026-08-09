using System.Reflection;
using PowerHelper.App.Services;
using PowerHelper.Core;
using PowerHelper.Abstractions;
using PowerHelper.Models;
using PowerHelper.Services;
#if WINDOWS
using PowerHelper.Windows;
#endif

namespace PowerHelper.App.Pages;

public partial class SettingsPage : ContentPage
{
    private const string RepoUrl = "https://github.com/dara-oladapo/power-helper";

    private readonly PowerHelperEngine _engine;

    // Guards the Toggled/ValueChanged handlers while control state is being populated
    // programmatically, which would otherwise re-save and re-apply settings that never
    // actually changed - and, for the startup switch, shell out to schtasks for it.
    private bool _suppressEvents;

    private bool _gpuActionInFlight;

    // The user's real accent, cached so the charge meter can flip between it and the
    // caution colour without re-reading the registry on every status tick.
    private Color _accentColor = Colors.SteelBlue;

    public SettingsPage(PowerHelperEngine engine)
    {
        _engine = engine;

        InitializeComponent();

        ApplyCapabilities();
        ApplyAccent();
        LoadFromSettings();
        Render(_engine.LastStatus);

        VersionLabel.Text = Assembly.GetExecutingAssembly().GetName().Version is { } version
            ? $"v{version.Major}.{version.Minor}.{version.Build}"
            : "dev build";

        _engine.StatusChanged += OnEngineStatusChanged;
        _engine.SettingsChanged += OnEngineSettingsChanged;
        _engine.UpdateFound += OnEngineUpdateFound;
    }

    // ---------------------------------------------------------------- capability gating

    /// <summary>
    /// A control for something this machine can't do is disabled, and its description is
    /// replaced with the reason the platform gave.
    ///
    /// The wording comes from the implementation rather than from here on purpose: only the
    /// implementation knows whether the answer is "no NVIDIA adapter on this laptop", "this
    /// panel has one refresh rate" or "macOS has no API for it", and a UI that guesses
    /// between those ends up saying something untrue on one of them.
    /// </summary>
    private void ApplyCapabilities()
    {
        Gate(_engine.GpuSupport, GpuRowDescription, AutoDisableGpuSwitch, ManualGpuButton);
        GpuChip.IsVisible = _engine.GpuSupport.IsSupported;

        Gate(_engine.PowerProfileSupport, PowerPlanDescription, PowerPlanSwitch);
        Gate(_engine.RefreshRateSupport, RefreshRateDescription, RefreshRateSwitch);
        Gate(_engine.BrightnessSupport, BrightnessDescription, BrightnessSwitch, BrightnessSlider);
        Gate(_engine.StartupSupport, StartupDescription, StartupSwitch);

        // "Power saver" on Windows, "Low Power Mode" on macOS - the platform names its own
        // battery profile, because ours would be wrong on at least one of them.
        PowerPlanTitle.Text = $"Match the {_engine.BatteryProfileName} profile to the power source";
    }

    private static void Gate(CapabilitySupport support, Label description, params View[] controls)
    {
        if (support.IsSupported)
        {
            return;
        }

        foreach (var control in controls)
        {
            control.IsEnabled = false;
        }

        if (support.Reason is { Length: > 0 } reason)
        {
            description.Text = reason;
        }
    }

    /// <summary>
    /// The charge meter is the only thing on this page that paints its own accent fill -
    /// every control gets the system accent from the platform for free. Re-applied whenever
    /// the user changes their accent or app mode.
    ///
    /// Windows publishes the accent in the registry, so the meter can match the rest of the
    /// desktop exactly. Mac Catalyst has no equivalent a third-party app can read, so the
    /// meter falls back to the same blue Windows ships as its default - deliberately a plain
    /// value rather than a guess at what the user picked.
    ///
    /// Which shade of the accent is picked follows the theme the app is actually painting,
    /// not the OS app mode: Windows publishes a lighter accent for dark surfaces and a darker
    /// one for light ones, and a window pinned to Light on a dark desktop needs the latter.
    /// </summary>
    public void ApplyAccent()
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;

#if WINDOWS
        var accent = SystemThemeService.CurrentAccent(dark ? AppThemeMode.Dark : AppThemeMode.Light);
        _accentColor = Color.FromRgb(accent.R, accent.G, accent.B);
#else
        _accentColor = dark ? Color.FromArgb("#4CC2FF") : Color.FromArgb("#005A9E");
#endif
        Render(_engine.LastStatus);
    }

    // ---------------------------------------------------------------- rendering

    private void OnEngineStatusChanged(StatusSnapshot snapshot) =>
        MainThread.BeginInvokeOnMainThread(() => Render(snapshot));

    private void OnEngineSettingsChanged() =>
        MainThread.BeginInvokeOnMainThread(LoadFromSettings);

    private void OnEngineUpdateFound(UpdateInfo update) => MainThread.BeginInvokeOnMainThread(() =>
    {
        UpdateLabel.Text = $"Update available: v{update.LatestVersion}";
        UpdateLabel.IsVisible = true;
    });

    private void Render(StatusSnapshot snapshot)
    {
        var battery = snapshot.Battery;

        if (battery.BatteryPresent)
        {
            BatteryPercentLabel.Text = $"{battery.PercentCharged}%";

            // The description already leads with the percentage for the tray's one-line
            // tooltip; strip it here so the figure isn't printed twice, once at 32px and
            // again immediately underneath.
            BatteryDetailLabel.Text = TrimLeadingPercentage(battery.Description);
        }
        else
        {
            BatteryPercentLabel.Text = "—";
            BatteryDetailLabel.Text = battery.Description;
        }

        // No battery at all means mains-powered, not "on battery" - PluggedIn is false in
        // both cases, so the presence check has to come first.
        PowerSourceLabel.Text = !battery.BatteryPresent || battery.PluggedIn ? "On AC power" : "On battery";

        if (snapshot.GpuPresent)
        {
            GpuStateLabel.Text = snapshot.GpuEnabled ? "Discrete GPU on" : "Discrete GPU off";
            if (!_gpuActionInFlight)
            {
                ManualGpuButton.Text = snapshot.GpuEnabled ? "Disable now" : "Enable now";
            }
        }

        UpdateChargeMeter(snapshot);
    }

    private void UpdateChargeMeter(StatusSnapshot snapshot)
    {
        var battery = snapshot.Battery;
        var percent = battery.BatteryPresent ? Math.Clamp(battery.PercentCharged, 0, 100) : 0;

        ChargeMeter.ColumnDefinitions[0].Width = new GridLength(percent, GridUnitType.Star);
        ChargeMeter.ColumnDefinitions[1].Width = new GridLength(100 - percent, GridUnitType.Star);

        // Below the user's own warning threshold, and actually discharging, the fill turns
        // caution. This is the only colour on the page that means something.
        var low = battery.BatteryPresent
            && !battery.PluggedIn
            && percent <= _engine.Settings.LowBatteryWarningThresholdPercent;

        ChargeFill.Color = low ? Token("CautionText") : _accentColor;
    }

    private static string TrimLeadingPercentage(string description)
    {
        var separator = description.IndexOf('•');
        return separator >= 0 && separator + 1 < description.Length
            ? description[(separator + 1)..].Trim()
            : description;
    }

    private void LoadFromSettings()
    {
        _suppressEvents = true;

        var settings = _engine.Settings;

        AutoDisableGpuSwitch.IsToggled = settings.AutoDisableDgpuOnBattery;
        PowerPlanSwitch.IsToggled = settings.AutoSwitchPowerPlanOnBattery;
        RefreshRateSwitch.IsToggled = settings.ThrottleRefreshRateOnBattery;

        BrightnessSwitch.IsToggled = settings.CapBrightnessOnBattery;
        BrightnessSlider.Value = settings.BatteryBrightnessPercent;
        BrightnessValueLabel.Text = $"{settings.BatteryBrightnessPercent}%";

        LowBatterySwitch.IsToggled = settings.LowBatteryWarningEnabled;
        ThresholdSlider.Value = settings.LowBatteryWarningThresholdPercent;
        ThresholdValueLabel.Text = $"{settings.LowBatteryWarningThresholdPercent}%";

        // The picker's items are in ThemePreference's own order, so the enum is the index.
        ThemePicker.SelectedIndex = (int)settings.Theme;

        StartupSwitch.IsToggled = _engine.StartupRegistered;

        // The level rows follow the switch they belong to, so an off policy can't be
        // adjusted in a way that looks like it will take effect.
        BrightnessLevelRow.IsEnabled = _engine.BrightnessSupport.IsSupported && settings.CapBrightnessOnBattery;
        BrightnessLevelRow.Opacity = BrightnessLevelRow.IsEnabled ? 1 : 0.4;
        ThresholdRow.IsEnabled = settings.LowBatteryWarningEnabled;
        ThresholdRow.Opacity = ThresholdRow.IsEnabled ? 1 : 0.4;

        _suppressEvents = false;
    }

    // ---------------------------------------------------------------- messages

    private void ShowMessage(string text, bool critical)
    {
        MessageLabel.Text = text;
        MessageLabel.TextColor = Token(critical ? "CriticalText" : "CautionText");
        MessageStrip.BackgroundColor = Token(critical ? "CriticalFill" : "CautionFill");
        MessageStrip.Stroke = new SolidColorBrush(Token(critical ? "CriticalStroke" : "CautionStroke"));
        MessageStrip.IsVisible = true;
    }

    private void OnDismissMessageClicked(object? sender, EventArgs e) => MessageStrip.IsVisible = false;

    /// <summary>
    /// Resolves a palette token for the current app mode. Looked up rather than duplicated
    /// as literals here, so Colors.xaml stays the only place these values are written down.
    /// </summary>
    private Color Token(string key)
    {
        var themedKey = Application.Current?.RequestedTheme == AppTheme.Dark ? key + "Dark" : key;
        if (Application.Current?.Resources.TryGetValue(themedKey, out var value) == true && value is Color color)
        {
            return color;
        }

        return Colors.Gray;
    }

    // ---------------------------------------------------------------- handlers

    private void OnAutoDisableGpuToggled(object? sender, ToggledEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _engine.Settings.AutoDisableDgpuOnBattery = e.Value;
        _engine.NotifySettingsChanged();
    }

    private void OnPowerPlanToggled(object? sender, ToggledEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _engine.Settings.AutoSwitchPowerPlanOnBattery = e.Value;
        _engine.NotifySettingsChanged();
    }

    private void OnRefreshRateToggled(object? sender, ToggledEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _engine.Settings.ThrottleRefreshRateOnBattery = e.Value;
        _engine.NotifySettingsChanged();
    }

    private void OnBrightnessToggled(object? sender, ToggledEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _engine.Settings.CapBrightnessOnBattery = e.Value;
        _engine.NotifySettingsChanged();
    }

    private void OnLowBatteryToggled(object? sender, ToggledEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _engine.Settings.LowBatteryWarningEnabled = e.Value;
        _engine.NotifySettingsChanged();
    }

    // Slider handlers are split deliberately: the label follows the thumb continuously, but
    // nothing is written to disk or pushed at the hardware until the drag ends. The previous
    // version saved settings.json and re-applied brightness on every pixel of movement.
    private void OnBrightnessValueChanged(object? sender, ValueChangedEventArgs e) =>
        BrightnessValueLabel.Text = $"{Snap(e.NewValue, 5)}%";

    private void OnBrightnessDragCompleted(object? sender, EventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        var percent = Snap(BrightnessSlider.Value, 5);
        _engine.Settings.BatteryBrightnessPercent = percent;
        BrightnessValueLabel.Text = $"{percent}%";
        _engine.NotifySettingsChanged();
    }

    private void OnThresholdValueChanged(object? sender, ValueChangedEventArgs e) =>
        ThresholdValueLabel.Text = $"{Snap(e.NewValue, 5)}%";

    private void OnThresholdDragCompleted(object? sender, EventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        var percent = Snap(ThresholdSlider.Value, 5);
        _engine.Settings.LowBatteryWarningThresholdPercent = percent;
        ThresholdValueLabel.Text = $"{percent}%";
        _engine.NotifySettingsChanged();
    }

    // MAUI's Slider has no tick or snap support, so the values it produces are rounded here
    // instead. Both settings are percentages a user thinks about in fives.
    private static int Snap(double value, int step) => (int)(Math.Round(value / step) * step);

    private async void OnManualGpuClicked(object? sender, EventArgs e)
    {
        if (_gpuActionInFlight)
        {
            return;
        }

        _gpuActionInFlight = true;
        ManualGpuButton.IsEnabled = false;
        ManualGpuButton.Text = "Working…";

        try
        {
            var result = await _engine.ToggleGpuManuallyAsync();

            if (result == GpuActionResult.Failed)
            {
                ShowMessage(
                    "Couldn't switch the discrete GPU. Windows refused the device change — this usually means another process is holding the adapter, or the app isn't running elevated.",
                    critical: true);
            }
        }
        finally
        {
            _gpuActionInFlight = false;
            ManualGpuButton.IsEnabled = _engine.GpuSupport.IsSupported;
            Render(_engine.LastStatus);
        }
    }

    private async void OnStartupToggled(object? sender, ToggledEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        StartupSwitch.IsEnabled = false;
        try
        {
            var succeeded = await _engine.SetStartupAsync(e.Value);
            if (!succeeded)
            {
                ShowMessage(
                    e.Value
                        ? "Couldn't register the logon task. Creating an elevated scheduled task needs administrator rights."
                        : "Couldn't remove the logon task. It may have already been deleted outside the app.",
                    critical: false);
            }
        }
        finally
        {
            StartupSwitch.IsEnabled = true;

            // Reflect what schtasks actually did rather than what was asked for.
            _suppressEvents = true;
            StartupSwitch.IsToggled = _engine.StartupRegistered;
            _suppressEvents = false;
        }
    }

    /// <summary>
    /// Saves the choice and nothing else. Repainting is <c>App</c>'s job: it owns
    /// <c>UserAppTheme</c>, and the title bar and tray menu that have to be told about it are
    /// outside this page entirely.
    /// </summary>
    private void OnThemeSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        // -1 while the picker is being populated, and not a value to persist.
        if (ThemePicker.SelectedIndex is < 0 or > (int)ThemePreference.Dark)
        {
            return;
        }

        var choice = (ThemePreference)ThemePicker.SelectedIndex;
        if (choice == _engine.Settings.Theme)
        {
            return;
        }

        _engine.Settings.Theme = choice;
        _engine.NotifySettingsChanged();
    }

    private void OnGitHubTapped(object? sender, TappedEventArgs e) => BrowserLauncher.Open(RepoUrl);

    private void OnUpdateTapped(object? sender, TappedEventArgs e)
    {
        if (_engine.AvailableUpdate is { } update)
        {
            BrowserLauncher.Open(update.ReleaseUrl);
        }
    }
}
