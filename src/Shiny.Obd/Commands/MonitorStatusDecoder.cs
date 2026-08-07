namespace Shiny.Obd.Commands;

/// <summary>
/// Decodes the four bytes of a mode 01 PID 0x01 / 0x41 reply into a <see cref="MonitorStatus"/>.
/// </summary>
/// <remarks>
/// Split out from the commands so the bit layout can be asserted on without a transport, the same
/// way <see cref="DtcDecoder"/> is.
/// <para>
/// The layout, which is the part worth writing down: byte A carries the lamp and the code count.
/// In byte B, bits 0-2 say whether the three common monitors are <i>supported</i> and bits 4-6 say
/// whether each is <i>incomplete</i> — note that the flag is inverted, so a set bit means still
/// running. Bit 3 selects which set of monitors bytes C and D describe. C and D then repeat that
/// supported/incomplete pairing for the eight type-specific monitors, MSB first.
/// </para>
/// </remarks>
public static class MonitorStatusDecoder
{
    // Byte B bits 0-2 (supported) pair with bits 4-6 (incomplete), in this order.
    static readonly EmissionMonitor[] CommonMonitors =
    [
        EmissionMonitor.Misfire,
        EmissionMonitor.FuelSystem,
        EmissionMonitor.Components
    ];

    // Bytes C/D, bit 7 down to bit 0.
    static readonly EmissionMonitor[] SparkMonitors =
    [
        EmissionMonitor.EgrOrVvtSystem,
        EmissionMonitor.OxygenSensorHeater,
        EmissionMonitor.OxygenSensor,
        EmissionMonitor.GasolineParticulateFilter,
        EmissionMonitor.SecondaryAirSystem,
        EmissionMonitor.EvaporativeSystem,
        EmissionMonitor.HeatedCatalyst,
        EmissionMonitor.Catalyst
    ];

    static readonly EmissionMonitor[] CompressionMonitors =
    [
        EmissionMonitor.EgrOrVvtSystem,
        EmissionMonitor.ParticulateFilter,
        EmissionMonitor.ExhaustGasSensor,
        EmissionMonitor.BoostPressure,
        EmissionMonitor.NoxOrScrAftertreatment,
        EmissionMonitor.NmhcCatalyst
    ];

    static readonly int[] SparkBits = [7, 6, 5, 4, 3, 2, 1, 0];

    // Bits 4 and 2 of bytes C/D are reserved on compression ignition, so that set skips them.
    static readonly int[] CompressionBits = [7, 6, 5, 3, 1, 0];

    /// <summary>
    /// Decodes the data bytes of a PID 0x01 or 0x41 reply (mode echo and PID already stripped).
    /// </summary>
    /// <remarks>
    /// A reply carrying only byte A still decodes — the lamp and the code count are read and the
    /// monitor list comes back empty. Some adapters truncate the reply that way, and the lamp is
    /// the more important half.
    /// </remarks>
    public static MonitorStatus Decode(byte[] data)
    {
        if (data.Length < 1)
            throw new ObdException("Monitor status response requires at least 1 data byte");

        var milOn = (data[0] & 0x80) != 0;
        var dtcCount = data[0] & 0x7F;

        if (data.Length < 4)
            return new MonitorStatus(milOn, dtcCount, IgnitionType.Spark, []);

        var b = data[1];
        var ignition = (b & 0x08) != 0 ? IgnitionType.Compression : IgnitionType.Spark;

        var monitors = new List<MonitorReadiness>(11);
        for (var i = 0; i < CommonMonitors.Length; i++)
        {
            var supported = (b & (1 << i)) != 0;
            if (supported)
            {
                // Bits 4-6 are set while the test is still *running*, so completion is the inverse
                var incomplete = (b & (1 << (i + 4))) != 0;
                monitors.Add(new MonitorReadiness(CommonMonitors[i], !incomplete));
            }
        }

        var names = ignition == IgnitionType.Compression ? CompressionMonitors : SparkMonitors;
        var bits = ignition == IgnitionType.Compression ? CompressionBits : SparkBits;

        for (var i = 0; i < names.Length; i++)
        {
            var mask = 1 << bits[i];
            var supported = (data[2] & mask) != 0;
            if (supported)
                monitors.Add(new MonitorReadiness(names[i], (data[3] & mask) == 0));
        }

        return new MonitorStatus(milOn, dtcCount, ignition, monitors);
    }
}
