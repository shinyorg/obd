namespace Shiny.Obd.Commands;

/// <summary>
/// Relative Accelerator Pedal Position (Mode 01, PID 0x5A) - Returns a percentage (0 to 100)
/// Formula: (A * 100) / 255
/// </summary>
/// <remarks>
/// The pedal position with the ECU's own learned rest position subtracted, so a released pedal
/// reads 0 and a floored one reads 100 without the caller having to discover this vehicle's
/// closed-pedal floor. Where it is supported it is the cleanest measure of driver input on
/// OBD-II — better than <see cref="AcceleratorPedalPositionCommand"/> for that purpose, and far
/// better than <see cref="ThrottlePositionCommand"/>, which reports the drive-by-wire output
/// rather than the request.
/// </remarks>
public class RelativeAcceleratorPedalPositionCommand() : ObdCommand<double>(0x01, 0x5A)
{
    protected override double ParseData(byte[] data)
    {
        if (data.Length < 1)
            throw new ObdException("Relative accelerator pedal position response requires 1 data byte");

        return data[0] * 100.0 / 255.0;
    }
}
