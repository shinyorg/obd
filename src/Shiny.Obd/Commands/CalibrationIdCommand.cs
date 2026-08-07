using System.Text;

namespace Shiny.Obd.Commands;

/// <summary>
/// Calibration ID (Mode 09, PID 0x04) - Returns the ECU software calibration identifiers
/// </summary>
/// <remarks>
/// The software the ECU is running, as up to four 16-byte ASCII identifiers (a vehicle with more
/// than one emissions-related controller reports one per controller). It changes when the ECU is
/// reflashed, which makes it the one thing on OBD-II that reveals a manufacturer software update
/// or an aftermarket tune — worth recording once per vehicle and comparing later, since a
/// calibration that changed between two visits explains behaviour that no sensor reading will.
/// </remarks>
public class CalibrationIdCommand() : ObdCommand<IReadOnlyList<string>>(0x09, 0x04)
{
    const int IdLength = 16;

    protected override IReadOnlyList<string> ParseData(byte[] data)
    {
        // The reply normally leads with a count of the identifiers that follow, but not every ECU
        // sends one. Each identifier is exactly 16 bytes, so the remainder decides it — the same
        // parity trick DtcDecoder uses, rather than assuming a transport.
        var payload = data.AsSpan();
        if (payload.Length % IdLength == 1)
            payload = payload[1..];

        if (payload.Length < IdLength)
            throw new ObdException("Calibration ID response requires at least 16 data bytes");

        var ids = new List<string>(payload.Length / IdLength);
        for (var offset = 0; offset + IdLength <= payload.Length; offset += IdLength)
        {
            // Unused bytes are reported as nulls, and a padding-only block is not an identifier
            var id = Encoding.ASCII.GetString(payload.Slice(offset, IdLength)).Trim('\0').Trim();
            if (id.Length > 0)
                ids.Add(id);
        }
        return ids;
    }
}
