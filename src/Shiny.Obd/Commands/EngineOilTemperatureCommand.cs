namespace Shiny.Obd.Commands;

/// <summary>
/// Engine Oil Temperature (Mode 01, PID 0x5C) - Returns temperature in °C (-40 to 215)
/// Formula: A - 40
/// </summary>
/// <remarks>
/// Uncommon before about 2010, so treat its absence as normal rather than a fault.
/// </remarks>
public class EngineOilTemperatureCommand() : ObdCommand<int>(0x01, 0x5C)
{
    protected override int ParseData(byte[] data)
    {
        if (data.Length < 1)
            throw new ObdException("Oil temperature response requires 1 data byte");

        return data[0] - 40;
    }
}
