namespace Shiny.Obd.Commands;

/// <summary>
/// Names the standardised mode 06 monitor IDs (MIDs).
/// </summary>
/// <remarks>
/// The standard fixes MIDs up to 0xDF; above that they are manufacturer-defined and a name here would
/// be a guess, so <see cref="Describe"/> answers null rather than inventing one. Manufacturers also
/// publish their own MID definitions for the standardised ranges - GM's are the best known - which is
/// worth knowing when a test result carries a name from here but a value that only makes sense against
/// the manufacturer's own documentation.
/// </remarks>
public static class MonitorIds
{
    /// <summary>The blocks to probe for supported MIDs, each covering the 32 MIDs that follow it.</summary>
    public static readonly byte[] BlockMids = [0x00, 0x20, 0x40, 0x60, 0x80, 0xA0];

    /// <summary>
    /// The monitor a MID refers to, or null when it is reserved, manufacturer-defined, or outside the
    /// standard table.
    /// </summary>
    public static string? Describe(byte mid) => mid switch
    {
        >= 0x01 and <= 0x10 => $"Oxygen sensor monitor {BankSensor(mid - 0x01, 4)}",
        >= 0x21 and <= 0x24 => $"Catalyst monitor bank {mid - 0x20}",
        >= 0x31 and <= 0x34 => $"EGR monitor bank {mid - 0x30}",
        >= 0x35 and <= 0x38 => $"VVT monitor bank {mid - 0x34}",

        // The four EVAP MIDs are the leak sizes the monitor tests for, in inches of orifice diameter.
        // 0.020" is the small-leak test that catches a loose filler cap.
        0x39 => "EVAP monitor (cap off / 0.150\")",
        0x3A => "EVAP monitor (0.090\")",
        0x3B => "EVAP monitor (0.040\")",
        0x3C => "EVAP monitor (0.020\")",
        0x3D => "Purge flow monitor",

        >= 0x41 and <= 0x50 => $"Oxygen sensor heater monitor {BankSensor(mid - 0x41, 4)}",
        >= 0x61 and <= 0x64 => $"Heated catalyst monitor bank {mid - 0x60}",
        >= 0x71 and <= 0x74 => $"Secondary air monitor {mid - 0x70}",
        >= 0x81 and <= 0x84 => $"Fuel system monitor bank {mid - 0x80}",
        >= 0x85 and <= 0x86 => $"Boost pressure monitor bank {mid - 0x84}",
        >= 0x90 and <= 0x91 => $"NOx adsorber monitor bank {mid - 0x8F}",
        >= 0x98 and <= 0x99 => $"NOx catalyst monitor bank {mid - 0x97}",

        0xA1 => "Misfire monitor (general)",
        >= 0xA2 and <= 0xAD => $"Misfire monitor cylinder {mid - 0xA1}",
        >= 0xB0 and <= 0xB1 => $"PM filter monitor bank {mid - 0xAF}",

        _ => null
    };

    static string BankSensor(int index, int sensorsPerBank)
        => $"B{(index / sensorsPerBank) + 1}S{(index % sensorsPerBank) + 1}";
}
