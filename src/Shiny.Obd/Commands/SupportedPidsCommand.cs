namespace Shiny.Obd.Commands;

/// <summary>
/// Supported PIDs (Mode 01, PIDs 0x00/0x20/0x40/0x60/0x80/0xA0/0xC0) - Returns the PIDs the vehicle
/// answers in the 32-PID block following the one queried
/// </summary>
/// <remarks>
/// Querying an unsupported PID just returns NO DATA, so probing the blocks up front is what lets a
/// caller offer only the readings a given vehicle actually reports. Walk
/// <see cref="BlockPids"/> and stop at the first block the vehicle does not answer.
/// </remarks>
public class SupportedPidsCommand(byte basePid) : ObdCommand<IReadOnlyList<byte>>(0x01, basePid)
{
    /// <summary>The blocks to probe, each covering the 32 PIDs that follow it.</summary>
    public static readonly byte[] BlockPids = [0x00, 0x20, 0x40, 0x60, 0x80, 0xA0, 0xC0];

    protected override IReadOnlyList<byte> ParseData(byte[] data)
    {
        if (data.Length < 4)
            throw new ObdException("Supported-PID response requires 4 data bytes");

        var supported = new List<byte>(32);
        for (var i = 0; i < 32; i++)
        {
            // Bit 31 (MSB of the first byte) is Pid + 1, descending to Pid + 32
            var isSet = (data[i / 8] & (0x80 >> (i % 8))) != 0;
            if (isSet)
                supported.Add((byte)(this.Pid + i + 1));
        }
        return supported;
    }
}
