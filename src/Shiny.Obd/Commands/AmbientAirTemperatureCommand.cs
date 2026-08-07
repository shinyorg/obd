namespace Shiny.Obd.Commands;

/// <summary>
/// Ambient Air Temperature (Mode 01, PID 0x46) - Returns temperature in °C (-40 to 215)
/// Formula: A - 40
/// </summary>
/// <remarks>
/// The air outside the vehicle, which is not the same thing as
/// <see cref="IntakeAirTemperatureCommand"/> (PID 0x0F): intake air is measured after the engine
/// bay has warmed it and after a turbo has compressed it, so it reads well above ambient at a
/// standstill and swings with load. Use this one for anything about the weather the car is in, and
/// intake air for anything about what the engine is breathing.
/// </remarks>
public class AmbientAirTemperatureCommand() : ObdCommand<int>(0x01, 0x46)
{
    protected override int ParseData(byte[] data)
    {
        if (data.Length < 1)
            throw new ObdException("Ambient air temperature response requires 1 data byte");

        return data[0] - 40;
    }
}
