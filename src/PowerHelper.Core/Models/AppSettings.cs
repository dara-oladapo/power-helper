using System.Text.Json.Serialization;

namespace PowerHelper.Models;

/// <summary>
/// What the window should paint itself as. <see cref="System"/> means "whatever the OS is
/// currently set to, including when that changes at sunset"; the other two pin it.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ThemePreference>))]
public enum ThemePreference
{
    System,
    Light,
    Dark,
}

public sealed class AppSettings
{
    public bool AutoDisableDgpuOnBattery { get; set; } = true;

    public bool LaunchAtLogon { get; set; }

    public bool AutoSwitchPowerPlanOnBattery { get; set; }

    public bool LowBatteryWarningEnabled { get; set; } = true;

    public int LowBatteryWarningThresholdPercent { get; set; } = 15;

    public bool ThrottleRefreshRateOnBattery { get; set; }

    public bool CapBrightnessOnBattery { get; set; }

    public int BatteryBrightnessPercent { get; set; } = 50;

    /// <summary>
    /// Defaults to <see cref="ThemePreference.System"/>, which is also what every settings
    /// file written before this setting existed deserialises to — a missing property takes
    /// the field initialiser, so upgrading keeps the behaviour the app already had.
    /// </summary>
    public ThemePreference Theme { get; set; } = ThemePreference.System;
}
