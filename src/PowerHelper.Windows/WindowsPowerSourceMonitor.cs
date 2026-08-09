using System.Windows.Forms;
using Microsoft.Win32;
using PowerHelper.Abstractions;

namespace PowerHelper.Windows;

public sealed class WindowsPowerSourceMonitor : IPowerSourceMonitor
{
    public event Action<bool>? PowerSourceChanged;

    public WindowsPowerSourceMonitor()
    {
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public bool IsOnBattery() =>
        SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        // Battery/charge-level notifications also raise StatusChange; subscribers only care
        // about AC<->battery transitions, which IsOnBattery reflects.
        if (e.Mode == PowerModes.StatusChange)
        {
            PowerSourceChanged?.Invoke(IsOnBattery());
        }
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        PowerSourceChanged = null;
    }
}
