namespace Shiny.Obd.Commands;

/// <summary>
/// Mode 02 — the snapshot of conditions the ECU stored at the instant a trouble code was set.
/// </summary>
/// <remarks>
/// Mode 02 accepts the same PIDs as mode 01 and scales them identically, so there is no separate
/// command per reading: call <see cref="ObdCommand{T}.AsFreezeFrame"/> on the mode 01 command you
/// already have.
/// <code>
/// var dtc = await connection.Execute(FreezeFrameCommands.CausalDtc());
/// if (dtc != null)
/// {
///     var rpm = await connection.Execute(StandardCommands.EngineRpm.AsFreezeFrame());
///     var load = await connection.Execute(StandardCommands.CalculatedEngineLoad.AsFreezeFrame());
/// }
/// </code>
/// <b>Always check <see cref="CausalDtc"/> first.</b> When it answers null there is no stored
/// snapshot, and every other mode 02 reading is meaningless rather than merely absent — the frame
/// is zero-filled, so an engine load of 0% and a coolant temperature of -40 °C come back looking
/// like measurements.
/// </remarks>
public static class FreezeFrameCommands
{
    /// <summary>
    /// Mode 02 PID 0x02 — the trouble code that caused the freeze frame to be stored, or null when
    /// the vehicle has no snapshot at all.
    /// </summary>
    /// <param name="frame">The frame number. Vehicles almost always store only frame 0.</param>
    public static IObdCommand<string?> CausalDtc(byte frame = 0) => new CausalDtcCommand(frame);

    sealed class CausalDtcCommand(byte frame) : IObdCommand<string?>
    {
        public string RawCommand => $"0202{frame:X2}";

        public string? Parse(byte[] data)
        {
            if (data.Length < 5)
                throw new ObdException("Freeze frame DTC response too short");

            if (data[0] != 0x42)
                throw new ObdException($"Unexpected mode response: 0x{data[0]:X2}, expected 0x42");

            if (data[1] != 0x02)
                throw new ObdException($"Unexpected PID: 0x{data[1]:X2}, expected 0x02");

            if (data[2] != frame)
                throw new ObdException($"Unexpected freeze frame: 0x{data[2]:X2}, expected 0x{frame:X2}");

            // 0x0000 is the standard's "no snapshot stored", and it is also exactly what
            // DecodePair treats as padding — so a null here means both, which is what a caller
            // needs to know before reading anything else out of the frame.
            return DtcDecoder.DecodePair(data[3], data[4]);
        }
    }
}
