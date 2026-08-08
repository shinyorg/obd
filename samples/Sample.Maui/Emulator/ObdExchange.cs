namespace Sample.Maui.Emulator;

/// <summary>
/// One request and the reply the emulator produced for it, plus a plain-language note for the log on
/// the Adapter tab.
/// </summary>
/// <param name="Request">The command as received, normalised.</param>
/// <param name="Lines">The response lines, without the trailing prompt.</param>
/// <param name="Description">What the emulator did - shown in the live log.</param>
/// <param name="IsData">Whether the lines are an OBD data reply rather than an AT acknowledgement.</param>
public sealed record ObdExchange(
    string Request,
    IReadOnlyList<string> Lines,
    string Description,
    bool IsData = false
)
{
    public string Summary => String.Join(" | ", this.Lines);
}
