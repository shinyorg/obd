using System.Text.Json.Serialization;

namespace Shiny.Obd.Emulator;

/// <summary>
/// The <c>state</c> section of an <see cref="ObdEmulatorProfile"/>: overrides applied after the vehicle
/// is selected. A null property leaves the emulator's current value alone.
/// </summary>
public sealed class ObdEmulatorStateSnapshot
{
    /// <summary>What the adapter answers to <c>ATI</c>. Include "STN" to exercise the OBDLink profile.</summary>
    public string? AdapterIdentifier { get; init; }

    /// <summary>What the adapter answers to <c>AT@1</c>.</summary>
    public string? DeviceDescription { get; init; }

    public bool? MilOn { get; init; }
    public bool? MonitorsComplete { get; init; }
    public bool? CompressionIgnition { get; init; }
    public bool? FreezeFrameCaptured { get; init; }
    public string? FreezeFrameDtc { get; init; }
    public bool? EngineOff { get; init; }

    public IReadOnlyList<string>? StoredDtcs { get; init; }
    public IReadOnlyList<string>? PendingDtcs { get; init; }
    public IReadOnlyList<string>? PermanentDtcs { get; init; }

    public IReadOnlyList<ObdParameterSnapshot>? Parameters { get; init; }
}


/// <summary>
/// One parameter's values in a profile. Set the field that matches the parameter's
/// <see cref="ObdValueKind"/> - <see cref="Number"/> on a text PID is rejected rather than ignored.
/// </summary>
public sealed class ObdParameterSnapshot
{
    /// <summary>Written as two hex digits, e.g. "01".</summary>
    [JsonConverter(typeof(HexByteJsonConverter))]
    public required byte Mode { get; init; }

    /// <summary>Written as two hex digits, e.g. "0D".</summary>
    [JsonConverter(typeof(HexByteJsonConverter))]
    public required byte Pid { get; init; }

    public bool? Supported { get; init; }

    /// <summary>Engineering units, for <see cref="ObdValueKind.Number"/> parameters.</summary>
    public double? Number { get; init; }

    /// <summary>For <see cref="ObdValueKind.Text"/> parameters - VIN, ECU name, calibration ID.</summary>
    public string? Text { get; init; }

    /// <summary>Hex pairs, for <see cref="ObdValueKind.Hex"/> parameters.</summary>
    public string? Hex { get; init; }

    /// <summary>Payload bytes sent instead of the encoded value. Empty clears an override.</summary>
    public string? RawOverride { get; init; }
}
