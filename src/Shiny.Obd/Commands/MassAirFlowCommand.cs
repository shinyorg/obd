namespace Shiny.Obd.Commands;

/// <summary>
/// Mass Air Flow Rate (Mode 01, PID 0x10) - Returns grams per second (0 to 655.35)
/// Formula: ((A * 256) + B) / 100
/// </summary>
/// <remarks>
/// Widely supported on petrol engines. Note that it spikes roughly tenfold under acceleration, so
/// it is a poor basis for deriving fuel consumption unless it is sampled fast enough to follow
/// that swing — see <see cref="EngineFuelRateCommand"/>.
/// </remarks>
public class MassAirFlowCommand() : ObdCommand<double>(0x01, 0x10)
{
    protected override double ParseData(byte[] data)
    {
        if (data.Length < 2)
            throw new ObdException("Mass air flow response requires 2 data bytes");

        return ((data[0] * 256) + data[1]) / 100.0;
    }
}
