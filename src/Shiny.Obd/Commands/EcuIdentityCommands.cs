using System.Text;

namespace Shiny.Obd.Commands;

/// <summary>
/// Calibration Verification Numbers (Mode 09, PID 0x06) - Returns a checksum over the ECU's
/// emissions-related calibration, one per calibration ID
/// </summary>
/// <remarks>
/// The other half of <see cref="CalibrationIdCommand"/>, and the half that cannot be faked by renaming
/// a file: the CVN is computed over the calibration itself, so a reflash that keeps the same
/// calibration ID still changes it. Together they are how an inspection determines whether a vehicle
/// is running the software it is supposed to be.
///
/// <para>
/// Returned as uppercase hex rather than a number. Each CVN is four bytes and has no arithmetic
/// meaning — it is only ever compared for equality, and rendering it as an integer invites someone to
/// sort or subtract it.
/// </para>
/// </remarks>
public class CalibrationVerificationNumberCommand() : ObdCommand<IReadOnlyList<string>>(0x09, 0x06)
{
    const int CvnLength = 4;

    protected override IReadOnlyList<string> ParseData(byte[] data)
    {
        // Like the calibration IDs, the reply may or may not lead with a count of the blocks that
        // follow. The remainder decides it rather than an assumption about the vehicle.
        var payload = data.AsSpan();
        if (payload.Length % CvnLength == 1)
            payload = payload[1..];

        if (payload.Length < CvnLength)
            throw new ObdException("Calibration verification number response requires at least 4 data bytes");

        var numbers = new List<string>(payload.Length / CvnLength);
        for (var offset = 0; offset + CvnLength <= payload.Length; offset += CvnLength)
            numbers.Add(Convert.ToHexString(payload.Slice(offset, CvnLength)));

        return numbers;
    }
}

/// <summary>
/// ECU Name (Mode 09, PID 0x0A) - Returns the name the emissions controller reports for itself
/// </summary>
/// <remarks>
/// A 20-byte ASCII field, right-padded with nulls. Free-form and manufacturer-defined — useful for
/// telling several controllers apart on a vehicle that has more than one, and for a support log, but
/// never as an identifier to branch behaviour on.
/// </remarks>
public class EcuNameCommand() : ObdCommand<string>(0x09, 0x0A)
{
    protected override string ParseData(byte[] data)
    {
        if (data.Length < 1)
            throw new ObdException("ECU name response requires at least 1 data byte");

        // A leading count byte appears on some vehicles. 20 is the field width, so anything longer
        // starts with the count.
        var payload = data.Length > 20 ? data.AsSpan(data.Length - 20) : data.AsSpan();

        return Encoding.ASCII.GetString(payload).Trim('\0').Trim();
    }
}
