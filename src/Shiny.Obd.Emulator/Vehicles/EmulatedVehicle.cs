namespace Shiny.Obd.Emulator;

/// <summary>
/// A vehicle the emulator can pretend to be: a real VIN, the identity PIDs that have to agree with
/// it, and enough of a physical model for the driving scenario to move like that car.
/// </summary>
/// <remarks>
/// <para>
/// The VIN is the point of this type. Every app that stores a vehicle keys off the VIN, so an
/// emulator that answers a made-up one is useless the moment you try to decode it - and a decoder
/// answers "no such vehicle" rather than failing loudly, which looks like a bug in your app.
/// Every <see cref="VehicleCatalog"/> VIN decodes cleanly through NHTSA vPIC, which is the
/// decoder <c>Shiny.Obd</c> ships.
/// </para>
/// <para>
/// The rest exists so the VIN is not a lie. A VIN that decodes to a Cummins pickup on a car that
/// idles at 780 rpm, shifts at 4900 and reports spark-ignition monitors is a worse test fixture
/// than no VIN at all: it will pass code that reads the VIN and code that reads the bus, and fail
/// the code that compares them.
/// </para>
/// </remarks>
public sealed record EmulatedVehicle
{
    /// <summary>How the vehicle is named, e.g. "2018 Honda Civic LX".</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The VIN answered to mode 09 PID 02. Real WMI and descriptor, valid ISO 3779 check digit, and
    /// a decode verified against vPIC - see <see cref="VehicleCatalog"/>.
    /// </summary>
    public required string Vin { get; init; }

    /// <summary>What a VIN decode should come back with, so the app under test has something to check against.</summary>
    public required string Decodes { get; init; }

    /// <summary>Why this vehicle is in the catalog - the case it covers that the others do not.</summary>
    public required string Note { get; init; }

    // ---- Identity PIDs ------------------------------------------------------------------------

    /// <summary>Mode 01 PID 51, the J1979 fuel type. 1 gasoline, 4 diesel, 8 electric, 17 hybrid gasoline.</summary>
    public byte FuelTypeCode { get; init; } = 1;

    /// <summary>Mode 01 PID 1C. 1 is CARB OBD-II, 6 EOBD, 7 EOBD and OBD-II, 10 JOBD.</summary>
    public byte ObdStandard { get; init; } = 1;

    /// <summary>Mode 01 PID 52. Non-zero only where the vehicle is flex-fuel.</summary>
    public double EthanolPercent { get; init; }

    /// <summary>Mode 01 PID 5B. Zero - and unsupported - on a vehicle with no traction battery.</summary>
    public double HybridBatteryPercent { get; init; }

    /// <summary>
    /// Swaps bytes C and D of the readiness monitors to the compression-ignition set. True for diesel.
    /// </summary>
    public bool CompressionIgnition { get; init; }

    /// <summary>Mode 09 PID 0A.</summary>
    public required string EcuName { get; init; }

    /// <summary>Mode 09 PID 04.</summary>
    public required string CalibrationId { get; init; }

    /// <summary>
    /// Mode 01 PIDs this vehicle does not answer at all. A client walking the supported-PID masks
    /// will not see them, and a read gets NO DATA.
    /// </summary>
    /// <remarks>
    /// The most useful thing an emulator can do for an app is take a PID away. A dashboard that
    /// assumes every car reports fuel level breaks on an EV, and an app that assumes mass air flow
    /// breaks on plenty of ordinary cars - neither shows up if the emulator always answers.
    /// </remarks>
    public IReadOnlyList<byte> UnsupportedPids { get; init; } = [];

    /// <summary>
    /// Whether the vehicle answers mode 09 at all. False on an early OBD-II vehicle, where the VIN,
    /// calibration ID and ECU name all come back NO DATA.
    /// </summary>
    public bool ReportsVehicleInformation { get; init; } = true;

    /// <summary>Fault memory this vehicle starts with. Codes a vehicle of this kind actually throws.</summary>
    public IReadOnlyList<string> StoredDtcs { get; init; } = ["P0301", "P0420"];

    /// <summary>Pending codes - seen once, not yet confirmed.</summary>
    public IReadOnlyList<string> PendingDtcs { get; init; } = ["P0171"];

    /// <summary>The code the stored freeze frame was captured against. Null when nothing is stored.</summary>
    public string? FreezeFrameDtc { get; init; } = "P0301";

    // ---- Physical model -----------------------------------------------------------------------

    /// <summary>Kerb mass plus a driver, in kg. Sets how hard the car accelerates for a given force.</summary>
    public double MassKg { get; init; } = 1500;

    /// <summary>Engine displacement in litres. Mass air flow is computed from it, not looked up.</summary>
    public double DisplacementLitres { get; init; } = 2.0;

    /// <summary>Peak power in watts. Caps tractive force at speed.</summary>
    public double PeakPowerWatts { get; init; } = 105_000;

    /// <summary>Peak tractive force in newtons. Caps acceleration at low speed, where a car is traction limited.</summary>
    public double PeakTractiveForceN { get; init; } = 6_000;

    /// <summary>Warm idle speed. Zero on an EV, which has no idle at all.</summary>
    public double IdleRpm { get; init; } = 780;

    /// <summary>Tank size in litres, which is what turns a fuel rate into a falling fuel level.</summary>
    public double TankLitres { get; init; } = 55;

    /// <summary>Engine rpm per km/h in each gear, top gear last.</summary>
    public IReadOnlyList<double> GearRatios { get; init; } = [95, 55, 38, 29, 23, 19];

    /// <summary>Upshift rpm under no load. A diesel is done long before a petrol engine is.</summary>
    public double ShiftFloorRpm { get; init; } = 2400;

    /// <summary>Upshift rpm at full throttle.</summary>
    public double ShiftCeilingRpm { get; init; } = 4900;

    /// <summary>Downshift rpm.</summary>
    public double DownshiftRpm { get; init; } = 1250;

    /// <summary>Stoichiometric air-fuel ratio - 14.7 for petrol, 14.5 for diesel.</summary>
    public double AirFuelRatio { get; init; } = 14.7;

    /// <summary>Fuel density in g/L - 745 for petrol, 832 for diesel. With the AFR it turns air mass into L/h.</summary>
    public double FuelDensityGramsPerLitre { get; init; } = 745;

    /// <summary>
    /// No engine to model: speed still moves, but rpm, air flow, fuel and the rest are meaningless
    /// and the PIDs that report them are dropped from support.
    /// </summary>
    public bool IsElectric { get; init; }

    /// <summary>Both halves of the identity in one line, for a caption.</summary>
    public string Summary => $"{this.Vin} · {this.Decodes}";

    public override string ToString() => this.Name;
}
