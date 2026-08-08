namespace Shiny.Obd.Commands;

/// <summary>
/// OBD Standards (Mode 01, PID 0x1C) - Returns which OBD regulation the vehicle was built to conform to
/// </summary>
/// <remarks>
/// Read once per vehicle. It tells you which regulatory world a car is from, and that changes what the
/// rest of the data means: a EOBD vehicle answers a different set of PIDs from a CARB OBD-II one, a
/// heavy-duty (HD OBD) vehicle reports the compression-ignition monitor layout rather than the spark
/// one, and a "not OBD compliant" answer explains a vehicle that connects but reports almost nothing.
/// </remarks>
public class ObdStandardsCommand() : ObdCommand<byte>(0x01, 0x1C)
{
    protected override byte ParseData(byte[] data)
    {
        if (data.Length < 1)
            throw new ObdException("OBD standards response requires 1 data byte");

        return data[0];
    }
}

/// <summary>
/// Names the OBD standard codes reported by <see cref="ObdStandardsCommand"/>.
/// </summary>
public static class ObdStandards
{
    /// <summary>
    /// The standard for a code, or null when the code is reserved, unassigned, or outside the table.
    /// </summary>
    /// <remarks>
    /// Null rather than "Unknown", matching <see cref="FuelTypes.Describe"/>. These strings end up in
    /// front of users and in AI prompts, where a placeholder reads as a fact about the vehicle; an
    /// absence has to stay an absence.
    /// </remarks>
    public static string? Describe(byte code) => code switch
    {
        1 => "OBD-II as defined by the CARB",
        2 => "OBD as defined by the EPA",
        3 => "OBD and OBD-II",
        4 => "OBD-I",
        5 => "Not OBD compliant",
        6 => "EOBD (Europe)",
        7 => "EOBD and OBD-II",
        8 => "EOBD and OBD",
        9 => "EOBD, OBD and OBD-II",
        10 => "JOBD (Japan)",
        11 => "JOBD and OBD-II",
        12 => "JOBD and EOBD",
        13 => "JOBD, EOBD and OBD-II",
        14 => "OBD, EOBD and KOBD",
        15 => "OBD, OBD-II, EOBD and KOBD",
        17 => "Engine Manufacturer Diagnostics (EMD)",
        18 => "Engine Manufacturer Diagnostics Enhanced (EMD+)",
        19 => "Heavy Duty On-Board Diagnostics (Child/Partial) (HD OBD-C)",
        20 => "Heavy Duty On-Board Diagnostics (HD OBD)",
        21 => "World Wide Harmonized OBD (WWH OBD)",
        23 => "Heavy Duty Euro OBD Stage I without NOx control (HD EOBD-I)",
        24 => "Heavy Duty Euro OBD Stage I with NOx control (HD EOBD-I N)",
        25 => "Heavy Duty Euro OBD Stage II without NOx control (HD EOBD-II)",
        26 => "Heavy Duty Euro OBD Stage II with NOx control (HD EOBD-II N)",
        27 => "Heavy Duty ZEV",
        28 => "Brazil OBD Phase 1 (OBDBr-1)",
        29 => "Brazil OBD Phase 2 (OBDBr-2)",
        30 => "Korean OBD (KOBD)",
        31 => "India OBD I (IOBD I)",
        32 => "India OBD II (IOBD II)",
        33 => "Heavy Duty Euro OBD Stage VI (HD EOBD-IV)",
        34 => "OBD, OBD-II and HD OBD",
        35 => "Brazil OBD Phase 3 (OBDBr-3)",
        _ => null
    };

    /// <summary>
    /// Whether the code names a heavy-duty standard, which reports the compression-ignition monitor
    /// layout rather than the spark-ignition one.
    /// </summary>
    /// <remarks>
    /// Useful as a cross-check against <see cref="MonitorStatus.Ignition"/> — the two are derived
    /// independently, so a disagreement means one of them is being read wrong.
    /// </remarks>
    public static bool IsHeavyDuty(byte code) => code is 19 or 20 or 23 or 24 or 25 or 26 or 27 or 33 or 34;
}
