namespace Shiny.Obd.Commands;

/// <summary>
/// Clear Diagnostic Trouble Codes (Mode 04) - Returns whether the ECU acknowledged
/// </summary>
/// <remarks>
/// This clears stored codes and freeze-frame data, and as a side effect resets the emissions
/// readiness monitors — which can take several drive cycles to re-run and will fail an emissions
/// test in the meantime. Only ever issue it from an explicitly confirmed user action.
/// </remarks>
public class ClearDtcCommand : IObdCommand<bool>
{
    /// <summary>The shared instance; the command carries no state.</summary>
    public static readonly ClearDtcCommand Instance = new();

    public string RawCommand => "04";

    public bool Parse(byte[] data) => data.Length > 0 && data[0] == 0x44;
}
