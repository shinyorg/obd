namespace Shiny.Obd.Vin;

/// <summary>
/// What a VIN decoded to: the vehicle's identity and, where the registry has it, its powertrain and
/// body.
/// </summary>
/// <remarks>
/// <para>
/// Provider-neutral by design — nothing here is shaped by NHTSA's wire format, so a different
/// <see cref="IVinDecoder"/> can populate it without callers noticing. The numbers arrive as
/// <i>numbers</i> rather than as the strings a registry sends, because parsing them is a rule the
/// library should own rather than repeat in every consumer: registries report an invariant decimal
/// point, so a naive parse on a comma-decimal machine reads "3.5" as thirty-five and reports a
/// 35-litre engine.
/// </para>
/// <para>
/// <b>Every field is nullable, and null means the registry had nothing.</b> A blank is an absence,
/// never a claim — which matters because these values commonly end up in front of a user or in a
/// prompt, where "Unknown" would read as a fact about the car. Values a registry uses in place of
/// an empty string ("Not Applicable", "Not Available", "N/A") are stripped to null, which is the
/// usual answer for electrification on an ordinary petrol car.
/// </para>
/// <para>
/// Coverage falls off outside North America — plenty of VINs decode cleanly with a make and model
/// and nothing else. That is not a failure, and a caller should not treat a missing displacement as
/// one.
/// </para>
/// </remarks>
public record VinVehicle
{
    /// <summary>The manufacturer, e.g. "Mazda".</summary>
    public string? Make { get; init; }

    /// <summary>The model, e.g. "CX-5".</summary>
    public string? Model { get; init; }

    /// <summary>The trim level, where the registry distinguishes one.</summary>
    public string? Trim { get; init; }

    /// <summary>The model year. Bounded to 1900-2100, so a garbled decode drops out rather than landing.</summary>
    public int? ModelYear { get; init; }

    /// <summary>The primary fuel, e.g. "Gasoline", "Diesel".</summary>
    /// <remarks>
    /// Mode 01 PID 0x51 answers the same question off the bus, and on a rebadged or grey-import
    /// vehicle the ECU in front of you is the more trustworthy of the two.
    /// </remarks>
    public string? FuelType { get; init; }

    /// <summary>Hybrid or EV level, where the vehicle has one. Null on an ordinary combustion car.</summary>
    public string? Electrification { get; init; }

    /// <summary>Cylinder count. Bounded to 1-16.</summary>
    public int? EngineCylinders { get; init; }

    /// <summary>Displacement in litres. Bounded to 0-20.</summary>
    public double? EngineDisplacementLitres { get; init; }

    /// <summary>Rated power in horsepower. Bounded to 1-2000.</summary>
    public int? EngineHorsepower { get; init; }

    /// <summary>Drivetrain, e.g. "4WD/4-Wheel Drive".</summary>
    public string? DriveType { get; init; }

    /// <summary>Body style, e.g. "Sport Utility Vehicle (SUV)/Multi-Purpose Vehicle (MPV)".</summary>
    public string? BodyClass { get; init; }

    /// <summary>Transmission style, e.g. "Automatic".</summary>
    public string? TransmissionStyle { get; init; }

    /// <summary>Whether anything at all was identified. A decode naming neither make nor model is not usable.</summary>
    public bool IsUsable => !String.IsNullOrWhiteSpace(this.Make) || !String.IsNullOrWhiteSpace(this.Model);
}
