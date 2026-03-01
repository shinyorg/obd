namespace Shiny.Obd.Commands;

/// <summary>
/// Calculated Engine Load (Mode 01, PID 0x04) - Returns percentage (0-100%)
/// Formula: (A * 100) / 255
/// </summary>
public class CalculatedEngineLoadCommand : ObdCommand<double>
{
    public CalculatedEngineLoadCommand() : base(0x01, 0x04) { }
    protected override double ParseData(byte[] data) => (data[0] * 100.0) / 255.0;
}
