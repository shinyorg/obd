namespace Shiny.Obd.Commands;

/// <summary>
/// Control Module Voltage (Mode 01, PID 0x42) - Returns volts (0 to 65.535)
/// Formula: ((A * 256) + B) / 1000
/// </summary>
public class ControlModuleVoltageCommand() : ObdCommand<double>(0x01, 0x42)
{
    protected override double ParseData(byte[] data)
    {
        if (data.Length < 2)
            throw new ObdException("Voltage response requires 2 data bytes");

        return ((data[0] * 256) + data[1]) / 1000.0;
    }
}
