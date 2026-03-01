using System;

namespace Shiny.Obd;

/// <summary>
/// Base class for standard OBD-II commands that follow the Mode/PID pattern.
/// Validates the response header (mode echo + PID) and delegates data parsing to subclasses.
/// </summary>
/// <typeparam name="T">The type of the parsed result</typeparam>
public abstract class ObdCommand<T> : IObdCommand<T>
{
    protected ObdCommand(byte mode, byte pid)
    {
        this.Mode = mode;
        this.Pid = pid;
    }

    public byte Mode { get; }
    public byte Pid { get; }
    public virtual string RawCommand => $"{Mode:X2}{Pid:X2}";

    public T Parse(byte[] data)
    {
        if (data.Length < 2)
            throw new ObdException("Response too short");

        var expectedMode = (byte)(Mode + 0x40);
        if (data[0] != expectedMode)
            throw new ObdException($"Unexpected mode response: 0x{data[0]:X2}, expected 0x{expectedMode:X2}");

        if (data[1] != Pid)
            throw new ObdException($"Unexpected PID: 0x{data[1]:X2}, expected 0x{Pid:X2}");

        var payload = new byte[data.Length - 2];
        Array.Copy(data, 2, payload, 0, payload.Length);
        return ParseData(payload);
    }

    /// <summary>
    /// Parse the data bytes (after mode+PID header has been stripped)
    /// </summary>
    protected abstract T ParseData(byte[] data);
}
