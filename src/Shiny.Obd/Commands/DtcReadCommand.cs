namespace Shiny.Obd.Commands;

/// <summary>
/// Reads diagnostic trouble codes (Modes 03, 07 and 0A) - Returns SAE J2012 code strings such as
/// "P0301"
/// </summary>
/// <remarks>
/// These modes carry no PID, so this implements <see cref="IObdCommand{T}"/> directly rather than
/// deriving from <see cref="ObdCommand{T}"/>, which validates and strips a two-byte mode+PID header.
/// </remarks>
public class DtcReadCommand(byte mode) : IObdCommand<IReadOnlyList<string>>
{
    /// <summary>Mode 03 — confirmed/stored codes (these turn the MIL on).</summary>
    public static readonly DtcReadCommand Stored = new(0x03);

    /// <summary>Mode 07 — pending codes from the current or last drive cycle.</summary>
    public static readonly DtcReadCommand Pending = new(0x07);

    /// <summary>Mode 0A — permanent codes, which only the ECU can clear.</summary>
    public static readonly DtcReadCommand Permanent = new(0x0A);

    /// <summary>The OBD-II mode this command reads codes from.</summary>
    public byte Mode => mode;

    public string RawCommand => mode.ToString("X2");

    public IReadOnlyList<string> Parse(byte[] data) => DtcDecoder.Decode(data, (byte)(mode + 0x40));
}
