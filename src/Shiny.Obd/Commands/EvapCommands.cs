namespace Shiny.Obd.Commands;

/// <summary>
/// Commanded Evaporative Purge (Mode 01, PID 0x2E) - Returns how far open the ECU is asking the purge
/// valve to be
/// </summary>
/// <remarks>
/// The EVAP system is the single most common source of a check engine light, and P0455/P0442 ("large"
/// and "small" leak detected) are usually a loose or perished fuel filler cap rather than anything
/// expensive. Purge command plus <see cref="EvapVaporPressureCommand"/> is what distinguishes a real
/// leak from a valve that is not sealing.
/// </remarks>
public class CommandedEvaporativePurgeCommand() : ObdCommand<double>(0x01, 0x2E)
{
    protected override double ParseData(byte[] data)
    {
        if (data.Length < 1)
            throw new ObdException("Commanded evaporative purge response requires 1 data byte");

        return data[0] * 100.0 / 255.0;
    }
}

/// <summary>
/// Evap System Vapor Pressure (Mode 01, PID 0x32) - Returns fuel tank vapour pressure in pascals,
/// relative to atmosphere
/// </summary>
/// <remarks>
/// Signed and fine-grained: roughly ±8 kPa in 0.25 Pa steps. Negative is vacuum, which is what a
/// working system pulls when the purge valve opens — a tank that will not hold that vacuum is the
/// leak the monitor is looking for.
///
/// <para>
/// Three different PIDs are all called some variant of "evap system vapour pressure" and they are not
/// interchangeable: this one is signed pascals, <see cref="EvapVaporPressureWideRangeCommand"/> (0x54)
/// is signed pascals over a four times wider range, and
/// <see cref="AbsoluteEvapVaporPressureCommand"/> (0x53) is unsigned kilopascals measured against
/// vacuum rather than atmosphere. Probe with <see cref="SupportedPidsCommand"/> and use whichever the
/// vehicle answers; do not convert between them.
/// </para>
/// </remarks>
public class EvapVaporPressureCommand() : ObdCommand<double>(0x01, 0x32)
{
    protected override double ParseData(byte[] data)
    {
        if (data.Length < 2)
            throw new ObdException("Evap vapor pressure response requires 2 data bytes");

        // Two's complement across both bytes, then quarter-pascal steps. Reading this unsigned turns
        // every vacuum — the entire point of the measurement — into a large positive pressure.
        var raw = (short)((data[0] << 8) | data[1]);
        return raw / 4.0;
    }
}

/// <summary>
/// Absolute Evap System Vapor Pressure (Mode 01, PID 0x53) - Returns fuel tank vapour pressure in
/// kilopascals, measured against vacuum
/// </summary>
/// <remarks>
/// Unsigned and absolute, so around 101 kPa is atmospheric rather than zero — see the comparison note
/// on <see cref="EvapVaporPressureCommand"/>.
/// </remarks>
public class AbsoluteEvapVaporPressureCommand() : ObdCommand<double>(0x01, 0x53)
{
    protected override double ParseData(byte[] data)
    {
        if (data.Length < 2)
            throw new ObdException("Absolute evap vapor pressure response requires 2 data bytes");

        return ((data[0] << 8) | data[1]) / 200.0;
    }
}

/// <summary>
/// Evap System Vapor Pressure (Mode 01, PID 0x54) - Returns fuel tank vapour pressure in pascals over
/// a wide range
/// </summary>
/// <remarks>
/// Signed pascals at 1 Pa resolution across roughly ±32 kPa — the same measurement as
/// <see cref="EvapVaporPressureCommand"/> with four times the range and a quarter of the resolution.
/// </remarks>
public class EvapVaporPressureWideRangeCommand() : ObdCommand<double>(0x01, 0x54)
{
    protected override double ParseData(byte[] data)
    {
        if (data.Length < 2)
            throw new ObdException("Evap vapor pressure response requires 2 data bytes");

        return (short)((data[0] << 8) | data[1]);
    }
}
