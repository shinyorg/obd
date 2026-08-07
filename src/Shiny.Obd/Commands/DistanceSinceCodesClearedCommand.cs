namespace Shiny.Obd.Commands;

/// <summary>
/// Distance Travelled Since Codes Cleared (Mode 01, PID 0x31) - Returns whole km (0 to 65,535)
/// Formula: (A * 256) + B
/// </summary>
/// <remarks>
/// Widely supported, and the practical stand-in when the vehicle has no
/// <see cref="OdometerCommand"/> PID. It is whole kilometres, so its rounding dominates over short
/// distances.
/// </remarks>
public class DistanceSinceCodesClearedCommand() : ObdCommand<int>(0x01, 0x31)
{
    protected override int ParseData(byte[] data)
    {
        if (data.Length < 2)
            throw new ObdException("Distance response requires 2 data bytes");

        return (data[0] * 256) + data[1];
    }
}
