namespace Shiny.Obd.Commands;

/// <summary>
/// Odometer (Mode 01, PID 0xA6) - Returns total distance in km
/// Formula: ((A &lt;&lt; 24) | (B &lt;&lt; 16) | (C &lt;&lt; 8) | D) / 10
/// </summary>
/// <remarks>
/// Only present on vehicles built to the later revisions of SAE J1979. Check
/// <see cref="SupportedPidsCommand"/> before issuing it, and treat its absence as normal rather
/// than as a fault.
/// </remarks>
public class OdometerCommand() : ObdCommand<double>(0x01, 0xA6)
{
    protected override double ParseData(byte[] data)
    {
        if (data.Length < 4)
            throw new ObdException("Odometer response requires 4 data bytes");

        var raw = ((uint)data[0] << 24) | ((uint)data[1] << 16) | ((uint)data[2] << 8) | data[3];
        return raw / 10.0;
    }
}
