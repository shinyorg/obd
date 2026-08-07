namespace Shiny.Obd.Vin;

/// <summary>
/// Turns a VIN — typically the one read off the ECU with <c>StandardCommands.Vin</c> — into what the
/// vehicle actually is.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a seam rather than a concrete service. The shipped implementation is NHTSA's vPIC,
/// which is free, keyless and excellent for North America and thinner elsewhere — so anyone with a
/// commercial provider, a regional registry or an offline table can register their own without
/// touching the calling code. See <c>ServiceCollectionExtensions.AddVinDecoder</c>.
/// </para>
/// <para>
/// The contract is that this <b>never throws</b>. Callers are background enrichment, not features:
/// a vehicle whose VIN cannot be decoded is a vehicle the user can still name by hand, and being
/// offline is the ordinary case in a car rather than an error worth surfacing.
/// </para>
/// </remarks>
public interface IVinDecoder
{
    /// <summary>
    /// Decodes a VIN, or returns null when it is implausible, the lookup fails, or the provider
    /// cannot identify the vehicle. Never throws.
    /// </summary>
    /// <param name="vin">The VIN. Padding and case are normalised, so a raw ECU read is fine.</param>
    /// <param name="ct">Cancellation.</param>
    Task<VinVehicle?> Decode(string? vin, CancellationToken ct = default);
}
