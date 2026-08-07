using System.Text.Json.Serialization;

namespace Shiny.Obd.Vin;

/// <summary>
/// The NHTSA vPIC <c>DecodeVinValues</c> response. That endpoint returns one flat object per VIN;
/// the plain <c>DecodeVin</c> endpoint returns ~150 label/value rows instead, which is far more work
/// to consume for the same fields.
/// </summary>
/// <remarks>
/// Internal because it is a wire format, not an API. <see cref="VinVehicle"/> is what callers see,
/// so the provider can be replaced without a breaking change.
/// </remarks>
record VpicResponse
{
    [JsonPropertyName("Count")] public int Count { get; init; }
    [JsonPropertyName("Message")] public string? Message { get; init; }
    [JsonPropertyName("Results")] public List<VpicResult>? Results { get; init; }
}

/// <summary>One decoded vehicle from vPIC.</summary>
/// <remarks>
/// Property names are declared explicitly because vPIC is PascalCase and a host application's
/// serializer options may carry any naming policy at all — an explicit
/// <see cref="JsonPropertyNameAttribute"/> wins over a policy, so this stays correct regardless.
/// Unlisted fields are ignored.
/// </remarks>
record VpicResult
{
    [JsonPropertyName("Make")] public string? Make { get; init; }
    [JsonPropertyName("Model")] public string? Model { get; init; }
    [JsonPropertyName("ModelYear")] public string? ModelYear { get; init; }
    [JsonPropertyName("Trim")] public string? Trim { get; init; }

    [JsonPropertyName("FuelTypePrimary")] public string? FuelTypePrimary { get; init; }
    [JsonPropertyName("ElectrificationLevel")] public string? ElectrificationLevel { get; init; }
    [JsonPropertyName("EngineCylinders")] public string? EngineCylinders { get; init; }
    [JsonPropertyName("DisplacementL")] public string? DisplacementL { get; init; }
    [JsonPropertyName("EngineHP")] public string? EngineHp { get; init; }
    [JsonPropertyName("DriveType")] public string? DriveType { get; init; }
    [JsonPropertyName("BodyClass")] public string? BodyClass { get; init; }
    [JsonPropertyName("TransmissionStyle")] public string? TransmissionStyle { get; init; }

    /// <summary>
    /// "0" means success. vPIC answers <c>200 OK</c> for an unparseable VIN and reports the problem
    /// here, so this has to be checked — a transport-level success says nothing about the decode.
    /// </summary>
    [JsonPropertyName("ErrorCode")] public string? ErrorCode { get; init; }

    [JsonPropertyName("ErrorText")] public string? ErrorText { get; init; }
}

/// <summary>
/// The source-generated serializer for the vPIC wire format.
/// </summary>
/// <remarks>
/// The library carries its own context rather than borrowing the host application's: this assembly
/// is AOT- and trim-analyzed, and a decoder that only works when the consumer remembers to register
/// two internal DTOs is a decoder that fails on someone's shipped iOS build.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(VpicResponse))]
partial class VpicJsonContext : JsonSerializerContext;
