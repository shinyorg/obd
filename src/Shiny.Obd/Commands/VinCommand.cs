using System.Text;

namespace Shiny.Obd.Commands;

/// <summary>
/// Vehicle Identification Number (Mode 09, PID 0x02) - Returns 17-character VIN string
/// Response: first byte is data item count, remaining 17 bytes are ASCII VIN characters
/// </summary>
public class VinCommand : ObdCommand<string>
{
    public VinCommand() : base(0x09, 0x02) { }

    protected override string ParseData(byte[] data)
    {
        // First byte is the number of data items (always 1 for VIN)
        // Remaining bytes are the 17-character ASCII VIN
        if (data.Length < 2)
            throw new ObdException("VIN response too short");

        var vinBytes = new byte[data.Length - 1];
        System.Array.Copy(data, 1, vinBytes, 0, vinBytes.Length);
        return Encoding.ASCII.GetString(vinBytes).Trim('\0').Trim();
    }
}
