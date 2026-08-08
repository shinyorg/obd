namespace Shiny.Obd.Commands;

/// <summary>
/// In-Use Performance Tracking (Mode 09, PID 0x08 for spark ignition or 0x0B for compression) -
/// Returns how often each emissions monitor has actually run against how often it could have
/// </summary>
/// <remarks>
/// This is the regulator's question, not the driver's: it is not whether a monitor passed but whether
/// it ever gets the chance to run. Each monitor reports a numerator (times the monitor completed) and
/// a denominator (times the vehicle was driven in conditions where it should have), and the ratio is
/// what an in-use compliance programme audits.
///
/// <para>
/// A ratio that is persistently near zero on a car with no fault means the monitor's enabling
/// conditions are never met by how that vehicle is driven — short trips, mostly. That is also the real
/// explanation behind a car that will not reach emissions readiness no matter how long it is driven,
/// which <see cref="MonitorStatusCommand"/> can only report as "still incomplete".
/// </para>
///
/// <para>
/// Read <see cref="Spark"/> or <see cref="Compression"/> according to the engine —
/// <see cref="MonitorStatus.Ignition"/> tells you which, and so does
/// <see cref="ObdStandards.IsHeavyDuty"/>. Asking for the wrong one returns NO DATA rather than
/// mislabelled figures.
/// </para>
/// </remarks>
public class InUsePerformanceTrackingCommand(byte pid) : ObdCommand<InUsePerformanceTracking>(0x09, pid)
{
    /// <summary>Spark ignition (petrol) monitors — mode 09 PID 0x08.</summary>
    public static InUsePerformanceTrackingCommand Spark() => new(0x08);

    /// <summary>Compression ignition (diesel) monitors — mode 09 PID 0x0B.</summary>
    public static InUsePerformanceTrackingCommand Compression() => new(0x0B);

    /// <summary>
    /// The monitors reported by mode 09 PID 0x08, in the order the standard fixes.
    /// </summary>
    static readonly string[] SparkMonitors =
    [
        "Catalyst bank 1",
        "Catalyst bank 2",
        "Oxygen sensor bank 1",
        "Oxygen sensor bank 2",
        "EGR",
        "Secondary air",
        "Evaporative system",
        "Secondary oxygen sensor bank 1",
        "Secondary oxygen sensor bank 2"
    ];

    /// <summary>
    /// The monitors reported by mode 09 PID 0x0B, in the order the standard fixes.
    /// </summary>
    static readonly string[] CompressionMonitors =
    [
        "NMHC catalyst",
        "NOx catalyst",
        "NOx adsorber",
        "PM filter",
        "Exhaust gas sensor",
        "EGR / VVT",
        "Boost pressure",
        "Fuel system"
    ];

    protected override InUsePerformanceTracking ParseData(byte[] data)
    {
        // A count of the 16-bit data items that follow, then the items themselves. The count is in
        // words, not bytes, which is worth being explicit about: reading it as bytes halves the list
        // and silently drops the monitors at the end.
        var payload = data.AsSpan();
        if (payload.Length % 2 == 1)
            payload = payload[1..];

        if (payload.Length < 4)
            throw new ObdException("In-use performance tracking response requires at least 4 data bytes");

        var words = new int[payload.Length / 2];
        for (var i = 0; i < words.Length; i++)
            words[i] = (payload[i * 2] << 8) | payload[(i * 2) + 1];

        // The first two items are standalone counters, then completion/condition pairs.
        var names = this.Pid == 0x0B ? CompressionMonitors : SparkMonitors;
        var monitors = new List<InUsePerformanceRatio>();

        for (var i = 2; i + 1 < words.Length; i += 2)
        {
            var index = (i - 2) / 2;
            monitors.Add(new InUsePerformanceRatio(
                index < names.Length ? names[index] : null,
                words[i],
                words[i + 1]
            ));
        }

        return new InUsePerformanceTracking(words[0], words[1], monitors);
    }
}

/// <summary>The result of <see cref="InUsePerformanceTrackingCommand"/>.</summary>
/// <param name="MonitoringConditions">
/// OBDCOND - how many times the general OBD monitoring conditions have been met.
/// </param>
/// <param name="IgnitionCycles">IGNCNTR - how many ignition cycles the vehicle has completed.</param>
/// <param name="Monitors">One entry per emissions monitor, in the order the standard fixes.</param>
public readonly record struct InUsePerformanceTracking(
    int MonitoringConditions,
    int IgnitionCycles,
    IReadOnlyList<InUsePerformanceRatio> Monitors
);

/// <summary>One monitor's in-use performance ratio.</summary>
/// <param name="Monitor">
/// The monitor's name, or null when the vehicle reported more items than the standard names - a
/// position this library cannot identify is left unnamed rather than guessed at.
/// </param>
/// <param name="Completions">The numerator: times this monitor ran to completion.</param>
/// <param name="Conditions">The denominator: times the vehicle met the conditions for it to run.</param>
public readonly record struct InUsePerformanceRatio(string? Monitor, int Completions, int Conditions)
{
    /// <summary>
    /// Completions divided by conditions, or null when the conditions have never been met.
    /// </summary>
    /// <remarks>
    /// Null is the meaningful answer for a zero denominator, not zero: "this monitor never had the
    /// opportunity" and "this monitor had the opportunity and never ran" are different findings, and
    /// only the second is a problem with the vehicle.
    /// </remarks>
    public double? Ratio => this.Conditions > 0 ? (double)this.Completions / this.Conditions : null;
}
