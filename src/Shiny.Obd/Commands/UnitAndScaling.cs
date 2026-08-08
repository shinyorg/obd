namespace Shiny.Obd.Commands;

/// <summary>
/// The unit and scaling identifier (UASID) table that mode 06 test results are encoded with.
/// </summary>
/// <remarks>
/// Mode 06 does not fix a unit per test. Every value carries a one-byte identifier saying how to scale
/// it and what it then means, so the same 16-bit number is 0.25 rpm per bit under one identifier and
/// 0.122 millivolts per bit under another. Decoding without this table produces numbers that look
/// entirely plausible and are wrong by three orders of magnitude.
///
/// <para>
/// Identifiers at 0x80 and above are the signed forms. That distinction is the one that matters most:
/// a small negative test value read as unsigned becomes a number near 65,535 and turns a passing test
/// into a spectacular failure.
/// </para>
/// </remarks>
public static class UnitAndScaling
{
    /// <summary>
    /// How to interpret a raw mode 06 value, or null when the identifier is outside the standard table.
    /// </summary>
    /// <remarks>
    /// Null rather than a guess, following <see cref="FuelTypes.Describe"/>. A caller with a null
    /// scaling still has <see cref="OnBoardTestResult.RawValue"/> and can compare it against the raw
    /// limits, which is the part of a mode 06 result that stays meaningful without the unit.
    /// </remarks>
    public static UnitScaling? Lookup(byte id) => id switch
    {
        0x01 => new(1, 0, "count"),
        0x02 => new(0.1, 0, "count"),
        0x03 => new(0.01, 0, "count"),
        0x04 => new(0.001, 0, "count"),
        0x05 => new(0.0000305, 0, "count"),
        0x06 => new(0.000305, 0, "count"),
        0x07 => new(0.25, 0, "rpm"),
        0x08 => new(0.01, 0, "km/h"),
        0x09 => new(1, 0, "km/h"),
        0x0A => new(0.122, 0, "mV"),
        0x0B => new(0.001, 0, "V"),
        0x0C => new(0.01, 0, "V"),
        0x0D => new(0.00390625, 0, "mA"),
        0x0E => new(0.001, 0, "A"),
        0x0F => new(0.01, 0, "A"),
        0x10 => new(1, 0, "ms"),
        0x11 => new(100, 0, "ms"),
        0x12 => new(1, 0, "s"),
        0x13 => new(1, 0, "mOhm"),
        0x14 => new(1, 0, "Ohm"),
        0x15 => new(1, 0, "kOhm"),
        0x16 => new(0.1, -40.0, "°C"),
        0x17 => new(0.01, 0, "kPa"),
        0x18 => new(0.0117, 0, "kPa"),
        0x19 => new(0.079, 0, "kPa"),
        0x1A => new(1, 0, "kPa"),
        0x1B => new(10, 0, "kPa"),
        0x1C => new(0.01, 0, "°"),
        0x1D => new(0.5, 0, "°"),
        0x1E => new(0.0000305, 0, "ratio"),
        0x1F => new(0.05, 0, "ratio"),
        0x20 => new(0.00390625, 0, "ratio"),
        0x21 => new(1, 0, "mHz"),
        0x22 => new(1, 0, "Hz"),
        0x23 => new(1, 0, "kHz"),
        0x24 => new(1, 0, "count"),
        0x25 => new(1, 0, "km"),
        0x26 => new(0.1, 0, "mV/ms"),
        0x27 => new(0.01, 0, "g/s"),
        0x28 => new(1, 0, "g/s"),
        0x29 => new(0.25, 0, "Pa/s"),
        0x2A => new(0.001, 0, "kg/h"),
        0x2B => new(1, 0, "count"),
        0x2C => new(0.01, 0, "g"),
        0x2D => new(0.01, 0, "mg"),
        0x2E => new(1, 0, "boolean"),
        0x2F => new(0.01, 0, "%"),
        0x30 => new(0.001526, 0, "%"),
        0x31 => new(0.001, 0, "L"),
        0x32 => new(0.0000305, 0, "in"),
        0x33 => new(0.00024414, 0, "ratio"),
        0x34 => new(1, 0, "min"),
        0x35 => new(10, 0, "ms"),
        0x36 => new(0.01, 0, "g"),
        0x37 => new(0.1, 0, "g"),
        0x38 => new(1, 0, "g"),
        0x39 => new(0.01, -327.68, "%"),
        0x3A => new(0.001, 0, "g"),
        0x3B => new(0.0001, 0, "g"),
        0x3C => new(0.1, 0, "µs"),
        0x3D => new(0.01, 0, "mA"),
        0x3E => new(0.00006103516, 0, "mm²"),
        0x3F => new(0.01, 0, "L"),
        0x40 => new(1, 0, "ppm"),
        0x41 => new(0.01, 0, "µA"),

        0x81 => new(1, 0, "count", true),
        0x82 => new(0.1, 0, "count", true),
        0x83 => new(0.01, 0, "count", true),
        0x84 => new(0.001, 0, "count", true),
        0x85 => new(0.0000305, 0, "count", true),
        0x86 => new(0.000305, 0, "count", true),
        0x87 => new(1, 0, "ppm", true),
        0x8A => new(0.122, 0, "mV", true),
        0x8B => new(0.001, 0, "V", true),
        0x8C => new(0.01, 0, "V", true),
        0x8D => new(0.00390625, 0, "mA", true),
        0x8E => new(0.001, 0, "A", true),
        0x90 => new(1, 0, "ms", true),
        0x96 => new(0.1, 0, "°C", true),
        0x99 => new(0.1, 0, "kPa", true),
        0x9C => new(0.01, 0, "°", true),
        0x9D => new(0.5, 0, "°", true),
        0xA8 => new(1, 0, "g/s", true),
        0xA9 => new(0.25, 0, "Pa/s", true),
        0xAD => new(0.01, 0, "mg", true),
        0xAE => new(0.1, 0, "mg", true),
        0xAF => new(0.01, 0, "%", true),
        0xB0 => new(0.003052, 0, "%", true),
        0xB1 => new(2, 0, "mV/s", true),
        0xFC => new(0.01, 0, "kPa", true),
        0xFD => new(0.001, 0, "kPa", true),
        0xFE => new(0.25, 0, "Pa", true),

        _ => null
    };
}

/// <summary>How to turn a raw mode 06 value into a measurement.</summary>
/// <param name="Scale">Multiplier applied to the raw value.</param>
/// <param name="Offset">Added after scaling.</param>
/// <param name="Unit">The unit the scaled value is in.</param>
/// <param name="Signed">Whether the raw 16-bit value is two's complement.</param>
public readonly record struct UnitScaling(double Scale, double Offset, string Unit, bool Signed = false)
{
    /// <summary>Applies this scaling to a raw 16-bit value.</summary>
    public double Apply(int raw)
    {
        var value = this.Signed ? (short)raw : raw;
        return (value * this.Scale) + this.Offset;
    }
}
