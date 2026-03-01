namespace Shiny.Obd.Commands;

/// <summary>
/// Throttle Position (Mode 01, PID 0x11) - Returns percentage (0-100%)
/// Formula: (A * 100) / 255
/// </summary>
public class ThrottlePositionCommand : ObdCommand<double>
{
    public ThrottlePositionCommand() : base(0x01, 0x11) { }
    protected override double ParseData(byte[] data) => (data[0] * 100.0) / 255.0;
}
