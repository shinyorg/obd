namespace Shiny.Obd.Commands;

/// <summary>
/// Vehicle Speed (Mode 01, PID 0x0D) - Returns speed in km/h (0-255)
/// </summary>
public class VehicleSpeedCommand : ObdCommand<int>
{
    public VehicleSpeedCommand() : base(0x01, 0x0D) { }
    protected override int ParseData(byte[] data) => data[0];
}
