namespace Shiny.Obd;

/// <summary>
/// Detected OBD adapter information returned by <see cref="ObdConnection.DetectedAdapter"/>
/// </summary>
public class ObdAdapterInfo
{
    public ObdAdapterInfo(string rawIdentifier, ObdAdapterType type)
    {
        this.RawIdentifier = rawIdentifier;
        this.Type = type;
    }

    /// <summary>
    /// The raw response from ATI (e.g. "ELM327 v1.5", "STN1110 v4.2")
    /// </summary>
    public string RawIdentifier { get; }

    /// <summary>
    /// The detected adapter family
    /// </summary>
    public ObdAdapterType Type { get; }
}

public enum ObdAdapterType
{
    Unknown,
    Elm327,
    ObdLink
}
