namespace Shiny.Obd.Commands;

/// <summary>
/// Time Run With MIL On (Mode 01, PID 0x4D) - Returns a TimeSpan (0 to 65,535 minutes)
/// Formula: (A * 256) + B minutes
/// </summary>
/// <remarks>
/// Engine running time since the check-engine light came on. Note the resolution is whole
/// <i>minutes</i>, not seconds like <see cref="RuntimeSinceStartCommand"/>.
/// </remarks>
public class TimeRunWithMilOnCommand() : ObdCommand<TimeSpan>(0x01, 0x4D)
{
    protected override TimeSpan ParseData(byte[] data)
    {
        if (data.Length < 2)
            throw new ObdException("Time run with MIL on response requires 2 data bytes");

        return TimeSpan.FromMinutes((data[0] * 256) + data[1]);
    }
}
