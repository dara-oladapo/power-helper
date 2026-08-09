using PowerHelper.Abstractions;

namespace PowerHelper.Windows;

/// <summary>
/// Composes the Windows implementations into the one bundle the engine takes.
///
/// Windows is the only platform where all seven capabilities are real, which is the reason
/// the app started here. Each implementation still reports its own support rather than
/// assuming: a laptop with no NVIDIA adapter, a display with one refresh rate, or a panel
/// that doesn't expose WMI brightness are all ordinary Windows machines, and each of those
/// answers is decided per-machine rather than per-OS.
/// </summary>
public static class WindowsPlatformServices
{
    public static PlatformServices Create() => new(
        new WindowsGpuController(),
        new WindowsPowerProfileController(),
        new WindowsRefreshRateController(),
        new WindowsBrightnessController(),
        new WindowsBatteryReader(),
        new WindowsPowerSourceMonitor(),
        new WindowsStartupManager());
}
