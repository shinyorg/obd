namespace Shiny.Obd.Commands;

/// <summary>
/// Engine Fuel Rate (Mode 01, PID 0x5E) - Returns litres per hour (0 to 3,276.75)
/// Formula: ((A * 256) + B) / 20
/// </summary>
/// <remarks>
/// Support is patchy — common on newer diesels, absent on plenty of petrol cars — so check
/// <see cref="SupportedPidsCommand"/> first. It is the only direct measure of fuel flow on
/// standard OBD-II; deriving one from <see cref="MassAirFlowCommand"/> instead trades a reading
/// that is missing for one that is wrong under load.
/// </remarks>
public class EngineFuelRateCommand() : ObdCommand<double>(0x01, 0x5E)
{
    protected override double ParseData(byte[] data)
    {
        if (data.Length < 2)
            throw new ObdException("Engine fuel rate response requires 2 data bytes");

        return ((data[0] * 256) + data[1]) / 20.0;
    }
}
