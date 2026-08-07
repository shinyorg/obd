namespace Shiny.Obd.Commands;

/// <summary>
/// Distance Travelled With MIL On (Mode 01, PID 0x21) - Returns whole km (0 to 65,535)
/// Formula: (A * 256) + B
/// </summary>
/// <remarks>
/// How far the vehicle has been driven since the check-engine light came on, which is the
/// difference between a fault that appeared this morning and one that has been ignored for two
/// thousand kilometres. Pair it with <see cref="TimeRunWithMilOnCommand"/> (PID 0x4D).
/// </remarks>
public class DistanceWithMilOnCommand() : ObdCommand<int>(0x01, 0x21)
{
    protected override int ParseData(byte[] data)
    {
        if (data.Length < 2)
            throw new ObdException("Distance with MIL on response requires 2 data bytes");

        return (data[0] * 256) + data[1];
    }
}
