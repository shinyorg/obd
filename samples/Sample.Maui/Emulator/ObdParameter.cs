using System.Globalization;
using Shiny.Obd;

namespace Sample.Maui.Emulator;

/// <summary>
/// How a parameter's value is edited in the UI - which in turn decides how it is encoded onto the wire.
/// </summary>
public enum ObdValueKind
{
    /// <summary>A scalar in engineering units (km/h, °C, %). The encoder scales it to raw bytes.</summary>
    Number,

    /// <summary>ASCII text (VIN, ECU name, calibration ID).</summary>
    Text,

    /// <summary>Raw bytes typed as hex. Used for PIDs whose payload is a bitfield or a record.</summary>
    Hex,

    /// <summary>Computed from elsewhere in the emulator state - eg monitor status follows the MIL and DTC list.</summary>
    Derived
}

/// <summary>
/// One thing a simulated ECU can answer with: a mode + PID, how its value is edited, and how that
/// value turns into the bytes an OBD client will read back.
/// </summary>
/// <remarks>
/// <see cref="Readback"/> decodes the emulator's own answer using the real <c>Shiny.Obd</c> command
/// object. That is deliberate - it means an encoder that disagrees with the library's parser shows up
/// in this app rather than as a wrong number in whatever app you are testing against it.
/// </remarks>
public partial class ObdParameter : ObservableObject
{
    public required string Name { get; init; }
    public required byte Mode { get; init; }
    public required byte Pid { get; init; }

    /// <summary>Engineering unit shown beside the editor. Null for unitless values.</summary>
    public string? Unit { get; init; }

    /// <summary>Free-text hint shown under the editor - byte layout for the hex kinds, caveats for the rest.</summary>
    public string? Note { get; init; }

    public ObdValueKind Kind { get; init; } = ObdValueKind.Number;

    public double Minimum { get; init; }
    public double Maximum { get; init; } = 100;

    /// <summary>Scales an engineering value to the PID's data bytes. Required for <see cref="ObdValueKind.Number"/>.</summary>
    public Func<double, byte[]>? FromNumber { get; init; }

    /// <summary>Encodes text to the PID's data bytes. Required for <see cref="ObdValueKind.Text"/>.</summary>
    public Func<string, byte[]>? FromText { get; init; }

    /// <summary>Produces the data bytes from emulator state. Required for <see cref="ObdValueKind.Derived"/>.</summary>
    public Func<byte[]>? FromState { get; init; }

    /// <summary>Runs the real library command over a full response so the UI can show what a client decodes.</summary>
    public Func<byte[], string>? Decode { get; init; }

    /// <summary>
    /// Mode 06 answers with the mode echo and then whole test records, which carry their own MID -
    /// there is no separate PID echo byte to emit.
    /// </summary>
    public bool EchoesPid { get; init; } = true;

    [ObservableProperty] bool isSupported = true;
    [ObservableProperty] double number;
    [ObservableProperty] string text = "";
    [ObservableProperty] string hex = "";

    /// <summary>
    /// Hex typed here replaces whatever the editor above would have produced. Every parameter has one
    /// so that a composite PID - or a deliberately malformed reply - can be sent without the catalog
    /// needing a bespoke editor for it.
    /// </summary>
    [ObservableProperty] string rawOverride = "";

    public string Request => $"{this.Mode:X2}{this.Pid:X2}";

    public string Title => this.Unit == null ? this.Name : $"{this.Name} ({this.Unit})";

    public bool IsNumber => this.Kind == ObdValueKind.Number;
    public bool IsText => this.Kind == ObdValueKind.Text;
    public bool IsHex => this.Kind == ObdValueKind.Hex;
    public bool HasNote => !String.IsNullOrWhiteSpace(this.Note);

    /// <summary>The PID's data bytes - everything after the mode echo and PID echo.</summary>
    public byte[] Payload()
    {
        if (TryParseHex(this.RawOverride, out var raw))
            return raw;

        return this.Kind switch
        {
            ObdValueKind.Number => this.FromNumber?.Invoke(this.Number) ?? [],
            ObdValueKind.Text => this.FromText?.Invoke(this.Text) ?? [],
            ObdValueKind.Derived => this.FromState?.Invoke() ?? [],
            _ => TryParseHex(this.Hex, out var hex) ? hex : []
        };
    }

    /// <summary>The complete response a client parses: mode echo, PID echo, then the payload.</summary>
    public byte[] Response()
    {
        var payload = this.Payload();
        var header = this.EchoesPid ? 2 : 1;
        var buffer = new byte[payload.Length + header];

        buffer[0] = (byte)(this.Mode + 0x40);
        if (this.EchoesPid)
            buffer[1] = this.Pid;

        payload.CopyTo(buffer, header);
        return buffer;
    }

    /// <summary>What a Shiny.Obd client decodes from the bytes this parameter is currently producing.</summary>
    public string Readback
    {
        get
        {
            if (this.Decode == null)
                return "-";

            try
            {
                return this.Decode(this.Response());
            }
            catch (Exception ex)
            {
                // A parameter that cannot be decoded is worth seeing rather than hiding: it is either a
                // deliberate malformed-reply test or a bug in this app's encoder.
                return $"! {ex.Message}";
            }
        }
    }

    public string ResponseHex => Convert.ToHexString(this.Response());

    partial void OnNumberChanged(double value) => this.RefreshReadback();
    partial void OnTextChanged(string value) => this.RefreshReadback();
    partial void OnHexChanged(string value) => this.RefreshReadback();
    partial void OnRawOverrideChanged(string value) => this.RefreshReadback();

    public void RefreshReadback()
    {
        this.OnPropertyChanged(nameof(this.Readback));
        this.OnPropertyChanged(nameof(this.ResponseHex));
    }

    /// <summary>
    /// Reads "41 0C" or "410C" into bytes. Anything that is not a whole run of hex pairs is rejected
    /// outright rather than half-parsed - a partially applied override would be far harder to explain
    /// than an empty one.
    /// </summary>
    public static bool TryParseHex(string? value, out byte[] bytes)
    {
        bytes = [];
        if (String.IsNullOrWhiteSpace(value))
            return false;

        var cleaned = value.Replace(" ", "").Replace("-", "").Replace("\t", "");
        if (cleaned.Length == 0 || cleaned.Length % 2 != 0)
            return false;

        var parsed = new byte[cleaned.Length / 2];
        for (var i = 0; i < parsed.Length; i++)
        {
            if (!Byte.TryParse(cleaned.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed[i]))
                return false;
        }

        bytes = parsed;
        return true;
    }
}
