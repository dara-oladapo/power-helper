using Microsoft.Win32;

namespace PowerHelper.Services;

public sealed class PowerMonitorService : IDisposable
{
    public event Action<bool>? PowerSourceChanged;

    public PowerMonitorService()
    {
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public bool IsOnBattery() =>
        SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        // Battery/charge-level notifications also raise StatusChange; PowerSourceChanged
        // subscribers only care about AC<->battery transitions, which IsOnBattery reflects.
        if (e.Mode == PowerModes.StatusChange)
        {
            PowerSourceChanged?.Invoke(IsOnBattery());
        }
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}
