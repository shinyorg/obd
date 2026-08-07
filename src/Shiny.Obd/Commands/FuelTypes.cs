namespace Shiny.Obd.Commands;

/// <summary>
/// The SAE J1979 fuel type table that <see cref="FuelTypeCommand"/> (PID 0x51) indexes into.
/// </summary>
/// <remarks>
/// Split out from the command so the table can be asserted on without a transport. An unlisted code
/// answers <c>null</c> rather than "Unknown": a caller storing or displaying this string needs to be
/// able to tell an absent answer from a claim about the vehicle. Code 0x00 is the table's own
/// "not available" and answers the same way.
/// </remarks>
public static class FuelTypes
{
    static readonly string?[] Table =
    [
        null,                       // 0x00 — not available
        "Gasoline",
        "Methanol",
        "Ethanol",
        "Diesel",
        "LPG",
        "CNG",
        "Propane",
        "Electric",
        "Bi-fuel, running gasoline",
        "Bi-fuel, running methanol",
        "Bi-fuel, running ethanol",
        "Bi-fuel, running LPG",
        "Bi-fuel, running CNG",
        "Bi-fuel, running propane",
        "Bi-fuel, running electricity",
        "Bi-fuel, running electric and combustion engine",
        "Hybrid gasoline",
        "Hybrid ethanol",
        "Hybrid diesel",
        "Hybrid electric",
        "Hybrid, running electric and combustion engine",
        "Hybrid regenerative",
        "Bi-fuel, running diesel"
    ];

    /// <summary>
    /// Describes a J1979 fuel type code, or null when the code is 0x00 (not available) or outside
    /// the table.
    /// </summary>
    public static string? Describe(byte code) => code < Table.Length ? Table[code] : null;

    /// <summary>
    /// Describes a J1979 fuel type code, or null when the PID was never reported.
    /// </summary>
    public static string? Describe(byte? code) => code.HasValue ? Describe(code.Value) : null;
}
