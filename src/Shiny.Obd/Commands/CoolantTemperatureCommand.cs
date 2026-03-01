namespace Shiny.Obd.Commands;

/// <summary>
/// Engine Coolant Temperature (Mode 01, PID 0x05) - Returns temperature in °C (-40 to 215)
/// Formula: A - 40
/// </summary>
public class CoolantTemperatureCommand : ObdCommand<int>
{
    public CoolantTemperatureCommand() : base(0x01, 0x05) { }
    protected override int ParseData(byte[] data) => data[0] - 40;
}
