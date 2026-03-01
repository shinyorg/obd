using System;

namespace Shiny.Obd.Commands;

/// <summary>
/// Run Time Since Engine Start (Mode 01, PID 0x1F) - Returns TimeSpan
/// Formula: (A * 256) + B seconds
/// </summary>
public class RuntimeSinceStartCommand : ObdCommand<TimeSpan>
{
    public RuntimeSinceStartCommand() : base(0x01, 0x1F) { }

    protected override TimeSpan ParseData(byte[] data)
    {
        var seconds = (data[0] * 256) + data[1];
        return TimeSpan.FromSeconds(seconds);
    }
}
