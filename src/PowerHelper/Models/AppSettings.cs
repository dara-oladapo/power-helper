namespace PowerHelper.Models;

public sealed class AppSettings
{
    public bool AutoDisableDgpuOnBattery { get; set; } = true;

    public bool LaunchAtLogon { get; set; }

    public bool AutoSwitchPowerPlanOnBattery { get; set; }

    public bool LowBatteryWarningEnabled { get; set; } = true;

    public int LowBatteryWarningThresholdPercent { get; set; } = 15;

    public bool ThrottleRefreshRateOnBattery { get; set; }
}
