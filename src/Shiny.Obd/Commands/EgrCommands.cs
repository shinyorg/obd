namespace Shiny.Obd.Commands;

/// <summary>
/// Commanded EGR (Mode 01, PID 0x2C) - Returns how far open the ECU is asking the EGR valve to be
/// </summary>
/// <remarks>
/// Read this with <see cref="EgrErrorCommand"/>, never alone. Commanded EGR on its own says only what
/// was asked for; the error says whether it happened. A P0401 (insufficient EGR flow) with 0% commanded
/// is a different fault from the same code with 40% commanded and a large error.
/// </remarks>
public class CommandedEgrCommand() : ObdCommand<double>(0x01, 0x2C)
{
    protected override double ParseData(byte[] data)
    {
        if (data.Length < 1)
            throw new ObdException("Commanded EGR response requires 1 data byte");

        return data[0] * 100.0 / 255.0;
    }
}

/// <summary>
/// EGR Error (Mode 01, PID 0x2D) - Returns the gap between commanded and actual EGR, as a percentage
/// of what was commanded
/// </summary>
/// <remarks>
/// Zero means the valve is doing what it was told. Positive means more flow than commanded, negative
/// means less — a persistently negative error under load is the classic carbon-clogged EGR passage,
/// and is usually visible here long before the code sets.
///
/// <para>
/// The scaling is the same 128-is-zero encoding as <see cref="FuelTrimCommand"/>. The value is
/// meaningless when nothing is commanded, so read <see cref="CommandedEgrCommand"/> alongside it.
/// </para>
/// </remarks>
public class EgrErrorCommand() : ObdCommand<double>(0x01, 0x2D)
{
    protected override double ParseData(byte[] data)
    {
        if (data.Length < 1)
            throw new ObdException("EGR error response requires 1 data byte");

        return (data[0] * 100.0 / 128.0) - 100.0;
    }
}
