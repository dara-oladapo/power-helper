using System.Management;
using System.Windows.Forms;
using PowerHelper.Abstractions;

namespace PowerHelper.Windows;

/// <summary>
/// Combines Windows' own discharge-time estimate (SystemInformation.PowerStatus, which
/// uses smoothing Windows already does internally - more accurate than a naive linear
/// projection) with the ACPI root\WMI battery classes for charge rate, which Windows'
/// public power APIs don't expose a time-to-full for at all.
/// </summary>
public sealed class WindowsBatteryReader : IBatteryReader
{
    private DateTime? _lastSampleTimeUtc;
    private uint? _lastSampleCapacity;
    private double _selfEstimatedChargeRatePerHour;

    public BatteryStatus GetStatus()
    {
        var powerStatus = SystemInformation.PowerStatus;
        if (powerStatus.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery))
        {
            return new BatteryStatus(false, 0, true, false, null, "No battery detected");
        }

        var percent = (int)Math.Round(powerStatus.BatteryLifePercent * 100);
        var pluggedIn = powerStatus.PowerLineStatus == PowerLineStatus.Online;

        if (!pluggedIn)
        {
            var seconds = powerStatus.BatteryLifeRemaining;
            if (seconds > 0)
            {
                var remaining = TimeSpan.FromSeconds(seconds);
                return new BatteryStatus(true, percent, false, false, remaining, $"{percent}% • {Format(remaining)} remaining");
            }

            return new BatteryStatus(true, percent, false, false, null, $"{percent}% • calculating time remaining");
        }

        var acpi = QueryAcpiBattery();

        if (!acpi.Charging)
        {
            // Not charging right now - drop any in-progress rate estimate so a future charge
            // session starts fresh instead of reusing a stale number.
            _lastSampleTimeUtc = null;
            _lastSampleCapacity = null;
            _selfEstimatedChargeRatePerHour = 0;
        }

        if (percent >= 99 || (acpi.FullCapacity > 0 && acpi.RemainingCapacity >= acpi.FullCapacity))
        {
            return new BatteryStatus(true, percent, true, false, null, $"{percent}% • fully charged");
        }

        if (acpi.Charging)
        {
            // Some hardware (confirmed on this machine) always reports ChargeRate=0 from
            // Windows' ACPI battery data, even mid-charge - not just briefly near-full. When
            // that happens, estimate the rate ourselves from successive capacity samples
            // instead of leaving the ETA permanently unavailable.
            var rate = acpi.ChargeRate > 0 ? acpi.ChargeRate : EstimateChargeRate(acpi.RemainingCapacity);

            if (rate > 0 && acpi.FullCapacity > acpi.RemainingCapacity)
            {
                var remaining = TimeSpan.FromHours((acpi.FullCapacity - acpi.RemainingCapacity) / rate);
                return new BatteryStatus(true, percent, true, true, remaining, $"{percent}% • {Format(remaining)} until full");
            }

            if (rate < 0)
            {
                // The EC's "Charging" flag can stay true even while the system draws more
                // power than the charger supplies, so capacity keeps falling despite it -
                // no ETA is possible here because there's no net charge happening to project.
                return new BatteryStatus(true, percent, true, true, null, $"{percent}% • charging, but power draw exceeds charger");
            }

            return new BatteryStatus(true, percent, true, true, null, $"{percent}% • charging (calculating time)");
        }

        // Plugged in, not charging, and not full: most commonly a charge-conservation cap.
        return new BatteryStatus(true, percent, true, false, null, $"{percent}% • plugged in, not charging");
    }

    private double EstimateChargeRate(uint currentCapacity)
    {
        var now = DateTime.UtcNow;

        if (_lastSampleTimeUtc is { } lastTime && _lastSampleCapacity is { } lastCapacity)
        {
            var elapsed = now - lastTime;
            // Require a meaningful gap so a noisy/rounded capacity delta over a tiny time
            // window doesn't produce a wildly wrong rate. A negative delta is kept (not
            // discarded) as a signal that the battery is net-draining despite "Charging".
            if (elapsed.TotalSeconds >= 20 && currentCapacity != lastCapacity)
            {
                _selfEstimatedChargeRatePerHour = (currentCapacity - lastCapacity) / elapsed.TotalHours;
            }
        }

        _lastSampleTimeUtc = now;
        _lastSampleCapacity = currentCapacity;

        return _selfEstimatedChargeRatePerHour;
    }

    private static AcpiBatteryReading QueryAcpiBattery()
    {
        try
        {
            using var statusSearcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM BatteryStatus");
            using var statusResults = statusSearcher.Get();
            using var statusEnumerator = statusResults.GetEnumerator();
            if (!statusEnumerator.MoveNext())
            {
                return default;
            }

            using var status = statusEnumerator.Current;
            var charging = (bool)status["Charging"];
            var remainingCapacity = Convert.ToUInt32(status["RemainingCapacity"]);
            var chargeRate = Convert.ToUInt32(status["ChargeRate"]);

            using var capacitySearcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM BatteryFullChargedCapacity");
            using var capacityResults = capacitySearcher.Get();
            using var capacityEnumerator = capacityResults.GetEnumerator();
            var fullCapacity = capacityEnumerator.MoveNext()
                ? Convert.ToUInt32(capacityEnumerator.Current["FullChargedCapacity"])
                : 0u;

            return new AcpiBatteryReading(charging, chargeRate, remainingCapacity, fullCapacity);
        }
        catch (ManagementException)
        {
            return default;
        }
    }

    private static string Format(TimeSpan span)
    {
        var hours = (int)span.TotalHours;
        var minutes = span.Minutes;
        return hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
    }

    private readonly record struct AcpiBatteryReading(bool Charging, uint ChargeRate, uint RemainingCapacity, uint FullCapacity);
}
