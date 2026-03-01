namespace Shiny.Obd.Commands;

/// <summary>
/// Fuel Tank Level Input (Mode 01, PID 0x2F) - Returns percentage (0-100%)
/// Formula: (A * 100) / 255
/// </summary>
public class FuelLevelCommand : ObdCommand<double>
{
    public FuelLevelCommand() : base(0x01, 0x2F) { }
    protected override double ParseData(byte[] data) => (data[0] * 100.0) / 255.0;
}
