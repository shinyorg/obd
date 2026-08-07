using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Shiny.Obd.Vin;

/// <summary>
/// Decodes a VIN through <b>NHTSA vPIC</b> — free, keyless, no registration.
/// </summary>
/// <remarks>
/// <para>
/// Coverage is a US federal registry, so it is excellent for North American vehicles and thinner
/// elsewhere: plenty of imports decode to a make and model and nothing more. That is not an error,
/// and this returns what it got rather than refusing the partial answer.
/// </para>
/// <para>
/// Every failure path is silent and answers null. Being out of signal is the ordinary case in a
/// vehicle, and a caller enriching a profile in the background has nothing useful to do with an
/// exception.
/// </para>
/// </remarks>
public class VpicVinDecoder(IHttpClientFactory httpClientFactory, ILogger<VpicVinDecoder>? logger = null)
    : IVinDecoder
{
    /// <summary>The <c>DecodeVinValues</c> endpoint, which returns one flat object per VIN.</summary>
    public const string Endpoint = "https://vpic.nhtsa.dot.gov/api/vehicles/DecodeVinValues/";

    /// <summary>
    /// Short on purpose. This typically runs while a user watches a connection settle, and a decode
    /// that has not answered by now is not worth waiting on.
    /// </summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>vPIC uses these in place of an empty string; none of them is a value.</summary>
    static readonly string[] NotValues = ["Not Applicable", "Not Available", "N/A", "null"];

    readonly ILogger logger = logger ?? NullLogger<VpicVinDecoder>.Instance;

    public async Task<VinVehicle?> Decode(string? vin, CancellationToken ct = default)
    {
        var normalized = VinNumber.Normalize(vin);
        if (!VinNumber.IsPlausible(normalized))
        {
            this.logger.LogDebug("VIN {Vin} is not plausible — not decoding", vin ?? "(null)");
            return null;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(RequestTimeout);

            using var http = httpClientFactory.CreateClient();
            var response = await http
                .GetFromJsonAsync($"{Endpoint}{normalized}?format=json", VpicJsonContext.Default.VpicResponse, cts.Token)
                .ConfigureAwait(false);

            var result = response?.Results?.FirstOrDefault();

            // vPIC answers 200 OK with an error payload for a VIN it cannot parse, so the transport
            // succeeding says nothing at all about the decode. ErrorCode may be a comma-separated
            // list, and "0" has to be the whole of it to mean clean.
            if (result == null ||
                (!String.IsNullOrWhiteSpace(result.ErrorCode) && result.ErrorCode.Trim() != "0"))
            {
                this.logger.LogInformation(
                    "vPIC could not identify VIN {Vin}: {Error}",
                    normalized,
                    result?.ErrorText ?? response?.Message ?? "no result"
                );
                return null;
            }

            var vehicle = Map(result);
            return vehicle.IsUsable ? vehicle : null;
        }
        catch (Exception ex)
        {
            // Offline is the normal case in a vehicle, not an error worth surfacing
            this.logger.LogDebug(ex, "VIN decode failed for {Vin}", normalized);
            return null;
        }
    }

    static VinVehicle Map(VpicResult result) => new()
    {
        Make = Clean(result.Make),
        Model = Clean(result.Model),
        Trim = Clean(result.Trim),
        ModelYear = ParseInt(result.ModelYear, 1900, 2100),
        FuelType = Clean(result.FuelTypePrimary),
        Electrification = Clean(result.ElectrificationLevel),
        EngineCylinders = ParseInt(result.EngineCylinders, 1, 16),
        EngineHorsepower = ParseInt(result.EngineHp, 1, 2000),
        EngineDisplacementLitres = ParseDouble(result.DisplacementL, 0, 20),
        DriveType = Clean(result.DriveType),
        BodyClass = Clean(result.BodyClass),
        TransmissionStyle = Clean(result.TransmissionStyle)
    };

    /// <summary>
    /// Bounded rather than "any number", because these values are shown to people and fed to models,
    /// where a garbled "402 cylinders" or a year 3 reads as a fact about the car rather than as a bad
    /// decode. Out of range is dropped to null — the same answer as "the registry had nothing".
    /// </summary>
    static int? ParseInt(string? value, int min, int max)
    {
        if (Clean(value) is not { } cleaned)
            return null;

        return Int32.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
               parsed >= min && parsed <= max
            ? parsed
            : null;
    }

    /// <summary>
    /// Parsed invariantly, and that is load-bearing rather than tidy: vPIC reports displacement as a
    /// decimal string with an invariant point ("3.5"), so <see cref="CultureInfo.CurrentCulture"/> on
    /// a comma-decimal machine reads it as thirty-five and reports a 35-litre engine.
    /// </summary>
    static double? ParseDouble(string? value, double min, double max)
    {
        if (Clean(value) is not { } cleaned)
            return null;

        return Double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
               parsed > min && parsed <= max
            ? parsed
            : null;
    }

    static string? Clean(string? value)
    {
        if (String.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return NotValues.Contains(trimmed, StringComparer.OrdinalIgnoreCase) ? null : trimmed;
    }
}
