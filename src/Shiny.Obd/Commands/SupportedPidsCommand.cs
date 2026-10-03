namespace Shiny.Obd.Commands;

/// <summary>
/// Supported PIDs (Mode 01, PIDs 0x00/0x20/0x40/0x60/0x80/0xA0/0xC0) - Returns the PIDs the vehicle
/// answers in the 32-PID block following the one queried
/// </summary>
/// <remarks>
/// Querying an unsupported PID just returns NO DATA, so probing the blocks up front is what lets a
/// caller offer only the readings a given vehicle actually reports. Walk
/// <see cref="BlockPids"/> and stop at the first block the vehicle does not answer.
/// <para>
/// Each ECU on the bus answers with its own mask. The result is their union — a PID is reported as
/// supported if any module answers it — so it does not depend on which module replied first.
/// </para>
/// </remarks>
public class SupportedPidsCommand(byte basePid) : ObdCommand<IReadOnlyList<byte>>(0x01, basePid)
{
    /// <summary>The blocks to probe, each covering the 32 PIDs that follow it.</summary>
    public static readonly byte[] BlockPids = [0x00, 0x20, 0x40, 0x60, 0x80, 0xA0, 0xC0];

    protected override IReadOnlyList<byte> ParseData(byte[] data)
    {
        if (data.Length < 4)
            throw new ObdException("Supported-PID response requires 4 data bytes");

        // ⚠️ Every ECU on the bus answers this request, and with headers off (ATH0) nothing says which
        // line came from which — the connection joins them into one run of bytes:
        //
        //     41 00 BE 3E B8 13    engine
        //     41 00 80 18 00 01    transmission
        //
        // Reading only the first four bytes reported whichever module happened to answer first, and the
        // order is not fixed. When it was the transmission, a caller walking the blocks lost most of the
        // engine's PIDs — and usually the "next block supported" bit with them — for the whole session.
        // A PID is supported if any module answers it, so every mask in the reply is merged.
        var mask = ReadMask(data, 0);
        for (var offset = 4; offset + 6 <= data.Length && data[offset] == 0x41 && data[offset + 1] == this.Pid; offset += 6)
            mask |= ReadMask(data, offset + 2);

        var supported = new List<byte>(32);
        for (var i = 0; i < 32; i++)
        {
            // Bit 31 (MSB of the first byte) is Pid + 1, descending to Pid + 32
            if ((mask & (0x80000000u >> i)) != 0)
                supported.Add((byte)(this.Pid + i + 1));
        }
        return supported;
    }

    static uint ReadMask(byte[] data, int offset)
        => (uint)data[offset] << 24 | (uint)data[offset + 1] << 16 | (uint)data[offset + 2] << 8 | data[offset + 3];
}
