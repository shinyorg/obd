namespace Shiny.Obd.Commands;

/// <summary>
/// Time Since Trouble Codes Cleared (Mode 01, PID 0x4E) - Returns a TimeSpan (0 to 65,535 minutes)
/// Formula: (A * 256) + B minutes
/// </summary>
/// <remarks>
/// Engine running time since codes were last cleared, in whole <i>minutes</i>. The time counterpart
/// to <see cref="DistanceSinceCodesClearedCommand"/> (PID 0x31), and the honest denominator for
/// anything trended "since the last reset" — a small figure here is also why
/// <see cref="MonitorStatus.IsReadyForInspection"/> may be false on a perfectly healthy vehicle.
/// </remarks>
public class TimeSinceCodesClearedCommand() : ObdCommand<TimeSpan>(0x01, 0x4E)
{
    protected override TimeSpan ParseData(byte[] data)
    {
        if (data.Length < 2)
            throw new ObdException("Time since codes cleared response requires 2 data bytes");

        return TimeSpan.FromMinutes((data[0] * 256) + data[1]);
    }
}
