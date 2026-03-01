namespace Shiny.Obd.Commands;

/// <summary>
/// Intake Air Temperature (Mode 01, PID 0x0F) - Returns temperature in °C (-40 to 215)
/// Formula: A - 40
/// </summary>
public class IntakeAirTemperatureCommand : ObdCommand<int>
{
    public IntakeAirTemperatureCommand() : base(0x01, 0x0F) { }
    protected override int ParseData(byte[] data) => data[0] - 40;
}
