using System.Diagnostics;
using System.Globalization;
using PowerHelper.Abstractions;

namespace PowerHelper.App.Platforms.MacCatalyst;

/// <summary>
/// One parsed reading of <c>pmset -g batt</c>, shared by the battery reader and the
/// power-source monitor so a single status refresh is one process launch rather than two.
///
/// <para>
/// pmset rather than IOKit: reaching IOPowerSources from Mac Catalyst means P/Invoking
/// CoreFoundation and hand-marshalling a CFDictionary, which is a lot of fragile surface
/// for data that a stable, documented command-line tool prints in two lines. If the launch
/// cost ever matters, IOKit is the upgrade path - the interface here doesn't change.
/// </para>
/// </summary>
internal sealed class MacPowerSource
{
    // pmset is a process launch, and IsOnBattery is asked far more often than the data
    // changes. A short cache keeps a status refresh to one launch without ever showing a
    // reading old enough to be wrong.
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(2);

    private readonly Lock _gate = new();

    private Reading _cached = Reading.Unknown;
    private DateTime _cachedAtUtc = DateTime.MinValue;

    public Reading Current
    {
        get
        {
            lock (_gate)
            {
                if (DateTime.UtcNow - _cachedAtUtc < CacheLifetime)
                {
                    return _cached;
                }

                _cached = Read();
                _cachedAtUtc = DateTime.UtcNow;
                return _cached;
            }
        }
    }

    internal readonly record struct Reading(
        bool BatteryPresent,
        int PercentCharged,
        bool OnBattery,
        bool Charging,
        TimeSpan? TimeRemaining)
    {
        // A machine we can't read is treated as mains-powered with no battery, because every
        // automatic policy keys off "am I on battery?" and the safe answer when we don't
        // know is the one that leaves the machine alone.
        public static Reading Unknown { get; } = new(false, 0, false, false, null);
    }

    private static Reading Read()
    {
        var output = RunPmset();
        if (output is null)
        {
            return Reading.Unknown;
        }

        // Line 1:  Now drawing from 'Battery Power'
        // Line 2:   -InternalBattery-0 (id=1234)\t87%; discharging; 3:21 remaining present: true
        var onBattery = output.Contains("'Battery Power'", StringComparison.OrdinalIgnoreCase);

        var percentIndex = output.IndexOf('%');
        if (percentIndex < 0)
        {
            // No battery line at all - a Mac mini or a Mac Studio.
            return new Reading(BatteryPresent: false, 0, onBattery, false, null);
        }

        var percent = ParsePercentEndingAt(output, percentIndex);
        var charging = output.Contains("; charging", StringComparison.OrdinalIgnoreCase);
        var remaining = ParseTimeRemaining(output);

        return new Reading(BatteryPresent: true, percent, onBattery, charging, remaining);
    }

    /// <summary>Walks back from the '%' to collect the digits immediately before it.</summary>
    private static int ParsePercentEndingAt(string text, int percentIndex)
    {
        var start = percentIndex;
        while (start > 0 && char.IsDigit(text[start - 1]))
        {
            start--;
        }

        return int.TryParse(text.AsSpan(start, percentIndex - start), out var value)
            ? Math.Clamp(value, 0, 100)
            : 0;
    }

    /// <summary>
    /// Reads the "H:MM remaining" figure. pmset prints 0:00 while it is still working the
    /// estimate out, which is not the same as "no time left" - that case returns null so the
    /// UI says "calculating" rather than claiming the machine is about to die.
    /// </summary>
    private static TimeSpan? ParseTimeRemaining(string text)
    {
        var marker = text.IndexOf(" remaining", StringComparison.OrdinalIgnoreCase);
        if (marker <= 0)
        {
            return null;
        }

        var end = marker;
        var start = end;
        while (start > 0 && (char.IsDigit(text[start - 1]) || text[start - 1] == ':'))
        {
            start--;
        }

        var span = text.AsSpan(start, end - start);
        var separator = span.IndexOf(':');
        if (separator <= 0)
        {
            return null;
        }

        if (!int.TryParse(span[..separator], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(span[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
        {
            return null;
        }

        var total = new TimeSpan(hours, minutes, 0);
        return total == TimeSpan.Zero ? null : total;
    }

    private static string? RunPmset()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/pmset",
                ArgumentList = { "-g", "batt" },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception)
        {
            // Sandboxed, missing, or refused. Indistinguishable from "no battery data" as
            // far as anything this app can do about it.
            return null;
        }
    }
}

internal sealed class MacBatteryReader(MacPowerSource source) : IBatteryReader
{
    public BatteryStatus GetStatus()
    {
        var reading = source.Current;

        if (!reading.BatteryPresent)
        {
            return new BatteryStatus(false, 0, true, false, null, "No battery detected");
        }

        var percent = reading.PercentCharged;
        var pluggedIn = !reading.OnBattery;

        if (reading.OnBattery)
        {
            return reading.TimeRemaining is { } remaining
                ? new BatteryStatus(true, percent, false, false, remaining, $"{percent}% • {Format(remaining)} remaining")
                : new BatteryStatus(true, percent, false, false, null, $"{percent}% • calculating time remaining");
        }

        if (percent >= 99 && !reading.Charging)
        {
            return new BatteryStatus(true, percent, true, false, null, $"{percent}% • fully charged");
        }

        if (reading.Charging)
        {
            return reading.TimeRemaining is { } untilFull
                ? new BatteryStatus(true, percent, true, true, untilFull, $"{percent}% • {Format(untilFull)} until full")
                : new BatteryStatus(true, percent, true, true, null, $"{percent}% • charging (calculating time)");
        }

        // Plugged in, not charging, not full - on a Mac this is normally Optimised Battery
        // Charging holding at 80%.
        return new BatteryStatus(true, percent, pluggedIn, false, null, $"{percent}% • plugged in, not charging");
    }

    private static string Format(TimeSpan span)
    {
        var hours = (int)span.TotalHours;
        return hours > 0 ? $"{hours}h {span.Minutes}m" : $"{span.Minutes}m";
    }
}

internal sealed class MacPowerSourceMonitor(MacPowerSource source) : IPowerSourceMonitor
{
    public event Action<bool>? PowerSourceChanged;

    public bool IsOnBattery() => source.Current.OnBattery;

    // No event is raised. macOS publishes power-source changes through IOKit's run-loop
    // notifications, which a Catalyst app can subscribe to but only via CoreFoundation
    // interop; until that exists the engine's own poll is what notices a transition. That
    // costs up to one poll interval of latency and nothing else, because every policy is
    // re-applied from scratch on each pass rather than being driven by the edge.
    public void Dispose() => PowerSourceChanged = null;
}
