namespace Shiny.Obd.Commands;

/// <summary>
/// Engine RPM (Mode 01, PID 0x0C) - Returns RPM as integer
/// Formula: ((A * 256) + B) / 4
/// </summary>
public class EngineRpmCommand : ObdCommand<int>
{
    public EngineRpmCommand() : base(0x01, 0x0C) { }
    protected override int ParseData(byte[] data) => ((data[0] * 256) + data[1]) / 4;
}
