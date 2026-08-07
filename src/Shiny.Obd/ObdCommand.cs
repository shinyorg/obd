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

    /// <summary>
    /// Reads this same PID out of a stored freeze frame (mode 02) instead of live (mode 01) — the
    /// snapshot of conditions the ECU recorded at the instant a trouble code was set.
    /// </summary>
    /// <param name="frame">The frame number. Vehicles almost always store only frame 0.</param>
    /// <remarks>
    /// Mode 02 accepts the same PIDs as mode 01 and scales them identically, so the parsing is
    /// reused rather than duplicated. The request carries the frame number and the response echoes
    /// it, which makes the header three bytes rather than two.
    /// <para>
    /// Check <c>FreezeFrameCommands.CausalDtc</c> first: when it answers null there is no snapshot
    /// stored, and every other mode 02 reading is meaningless rather than merely absent.
    /// </para>
    /// </remarks>
    /// <exception cref="ObdException">The command is not a mode 01 PID, so it has no freeze frame.</exception>
    public IObdCommand<T> AsFreezeFrame(byte frame = 0)
    {
        if (this.Mode != 0x01)
            throw new ObdException($"Freeze frames only exist for mode 01 PIDs; this command is mode 0x{Mode:X2}");

        return new FreezeFrame(this, frame);
    }

    sealed class FreezeFrame(ObdCommand<T> source, byte frame) : IObdCommand<T>
    {
        public string RawCommand => $"02{source.Pid:X2}{frame:X2}";

        public T Parse(byte[] data)
        {
            if (data.Length < 3)
                throw new ObdException("Freeze frame response too short");

            if (data[0] != 0x42)
                throw new ObdException($"Unexpected mode response: 0x{data[0]:X2}, expected 0x42");

            if (data[1] != source.Pid)
                throw new ObdException($"Unexpected PID: 0x{data[1]:X2}, expected 0x{source.Pid:X2}");

            if (data[2] != frame)
                throw new ObdException($"Unexpected freeze frame: 0x{data[2]:X2}, expected 0x{frame:X2}");

            var payload = new byte[data.Length - 3];
            Array.Copy(data, 3, payload, 0, payload.Length);
            return source.ParseData(payload);
        }
    }
}
