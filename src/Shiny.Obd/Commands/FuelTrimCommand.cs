namespace Shiny.Obd.Commands;

/// <summary>
/// Fuel Trim (Mode 01, PIDs 0x06/0x07/0x08/0x09) - Returns a percentage correction (-100 to 99.2)
/// Formula: (A * 100 / 128) - 100
/// </summary>
/// <remarks>
/// 128 is zero correction. Positive means the ECU is adding fuel (it reads the mixture as lean),
/// negative means it is pulling fuel out. Sustained drift in either direction is an early signal of
/// a vacuum leak, a lazy oxygen sensor or a fuel delivery problem, often long before a code is set.
/// Use the static factories rather than the constructor unless you need a bank this library has no
/// factory for.
/// </remarks>
public class FuelTrimCommand(byte pid) : ObdCommand<double>(0x01, pid)
{
    /// <summary>Short term fuel trim, bank 1 (PID 0x06).</summary>
    public static FuelTrimCommand ShortTermBank1() => new(0x06);

    /// <summary>Long term fuel trim, bank 1 (PID 0x07).</summary>
    public static FuelTrimCommand LongTermBank1() => new(0x07);

    /// <summary>Short term fuel trim, bank 2 (PID 0x08).</summary>
    public static FuelTrimCommand ShortTermBank2() => new(0x08);

    /// <summary>Long term fuel trim, bank 2 (PID 0x09).</summary>
    public static FuelTrimCommand LongTermBank2() => new(0x09);

    protected override double ParseData(byte[] data)
    {
        if (data.Length < 1)
            throw new ObdException("Fuel trim response requires 1 data byte");

        return (data[0] * 100.0 / 128.0) - 100.0;
    }
}
