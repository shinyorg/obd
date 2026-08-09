namespace Shiny.Obd.Emulator;

/// <summary>
/// The vehicles the emulator can be. Every VIN here is real in the ways that matter: a real WMI and
/// descriptor, a valid ISO 3779 check digit, and a decode confirmed against NHTSA vPIC - the
/// registry behind <c>VpicVinDecoder</c>.
/// </summary>
/// <remarks>
/// <para>
/// The serial portion (positions 12-17) is invented, which is the point: a VIN decodes from its WMI
/// and descriptor, so these identify a real make, model and year without belonging to a car anyone
/// owns. Nothing here is a vehicle identity you can trace to a person.
/// </para>
/// <para>
/// <b>Adding one.</b> Take the first eight characters and the last eight from the vehicle you want,
/// leave position 9 as anything, and run it through
/// <c>Shiny.Obd.Vin.VinNumber.CalculateCheckDigit</c> - that character goes in position 9. Then
/// check the whole VIN against
/// <c>https://vpic.nhtsa.dot.gov/api/vehicles/DecodeVinValues/&lt;vin&gt;?format=json</c> and keep
/// it only if <c>ErrorCode</c> comes back "0". Coverage is a US registry, so a European-market VIN
/// may decode to a make and nothing else.
/// </para>
/// </remarks>
public static class VehicleCatalog
{
    /// <summary>
    /// An ordinary, fully-supported modern petrol car - the one to develop against before trying the
    /// awkward ones.
    /// </summary>
    public static EmulatedVehicle HondaCivic { get; } = new()
    {
        Name = "2018 Honda Civic LX",
        Vin = "2HGFC2F51JH542108",
        Decodes = "2018 Honda Civic LX · 2.0L I4 · CVT",
        Note = "The baseline. Petrol, everything supported, nothing unusual to trip over.",
        EcuName = "ECM-EngineControl",
        CalibrationId = "37805-5AA-A010",
        MassKg = 1300,
        DisplacementLitres = 2.0,
        PeakPowerWatts = 118_000,
        PeakTractiveForceN = 5_200,
        IdleRpm = 700,
        TankLitres = 47,

        // A CVT has no steps, so it is faked with ten close ratios and a narrow band - the engine
        // holds revs and the road speed catches up, which is what the trace should look like.
        GearRatios = [78, 62, 50, 42, 36, 32, 28, 25, 22, 20],
        ShiftFloorRpm = 2100,
        ShiftCeilingRpm = 3800
    };

    /// <summary>A conventional torque-converter automatic, and the same car most people are driving.</summary>
    public static EmulatedVehicle ToyotaCamry { get; } = new()
    {
        Name = "2019 Toyota Camry LE",
        Vin = "4T1B11HK9KU742051",
        Decodes = "2019 Toyota Camry L/LE/SE/XLE · 2.5L I4 · Automatic",
        Note = "Eight-speed automatic - the stepped rpm sawtooth a real gearbox produces.",
        EcuName = "ECM-EngineControl",
        CalibrationId = "89663-06P10",
        MassKg = 1595,
        DisplacementLitres = 2.5,
        PeakPowerWatts = 151_000,
        PeakTractiveForceN = 6_400,
        IdleRpm = 650,
        TankLitres = 60,
        GearRatios = [105, 68, 48, 37, 30, 25, 21, 17]
    };

    /// <summary>A European ECU answering EOBD rather than CARB OBD-II on PID 1C.</summary>
    public static EmulatedVehicle Bmw330i { get; } = new()
    {
        Name = "2019 BMW 330i xDrive",
        Vin = "WBA5R1C58KA751236",
        Decodes = "2019 BMW 330i · 2.0L turbo I4 · AWD · Automatic",
        Note = "Reports EOBD and OBD-II on PID 1C, which is where region-specific display code gets caught out.",
        ObdStandard = 7,
        EcuName = "DME-MEVD17",
        CalibrationId = "0000A1B2C3",
        MassKg = 1725,
        DisplacementLitres = 2.0,
        PeakPowerWatts = 190_000,
        PeakTractiveForceN = 7_200,
        IdleRpm = 720,
        TankLitres = 59,
        GearRatios = [96, 62, 44, 34, 27, 22, 19, 16],
        StoredDtcs = ["P0300", "P052E"],
        PendingDtcs = ["P0128"],
        FreezeFrameDtc = "P0300"
    };

    /// <summary>Hybrid: fuel type 17, a traction battery on PID 5B, and battery codes in memory.</summary>
    public static EmulatedVehicle ToyotaPrius { get; } = new()
    {
        Name = "2016 Toyota Prius",
        Vin = "JTDKARFU5G3512804",
        Decodes = "2016 Toyota Prius Two Eco/Three/Four · 1.8L I4 · Strong HEV",
        Note = "Fuel type 17 (hybrid gasoline) and a live PID 5B. The engine stopping at rest is not modelled - idle holds.",
        FuelTypeCode = 17,
        HybridBatteryPercent = 62,
        EcuName = "HV-ECU-Prius",
        CalibrationId = "89663-47450",
        MassKg = 1425,
        DisplacementLitres = 1.8,
        PeakPowerWatts = 90_000,
        PeakTractiveForceN = 4_600,
        IdleRpm = 950,
        TankLitres = 43,

        // Planetary eCVT - the engine speed is barely related to road speed, so the ratios are close
        // together and the band is narrow.
        GearRatios = [64, 50, 41, 34, 29, 25, 22],
        ShiftFloorRpm = 1600,
        ShiftCeilingRpm = 3400,
        DownshiftRpm = 1050,
        StoredDtcs = ["P0A80", "P0420"],
        PendingDtcs = ["P3000"],
        FreezeFrameDtc = "P0A80"
    };

    /// <summary>
    /// Diesel: compression-ignition readiness monitors, no narrowband O2 sensors, no evaporative
    /// system, and an engine that is finished by 4000 rpm.
    /// </summary>
    public static EmulatedVehicle VolkswagenGolfTdi { get; } = new()
    {
        Name = "2015 VW Golf TDI",
        Vin = "3VWRA7AU1FM024518",
        Decodes = "2015 Volkswagen Golf · 2.0L TDI · Manual",
        Note = "Compression ignition, so bytes C and D of the readiness monitors describe a different set of tests.",
        FuelTypeCode = 4,
        ObdStandard = 6,
        CompressionIgnition = true,
        EcuName = "EDC17-Engine",
        CalibrationId = "04L906056",
        UnsupportedPids = [.. DieselDoesNotHave],
        MassKg = 1400,
        DisplacementLitres = 2.0,
        PeakPowerWatts = 110_000,
        PeakTractiveForceN = 6_800,
        IdleRpm = 820,
        TankLitres = 50,
        GearRatios = [110, 62, 42, 32, 26, 22],
        ShiftFloorRpm = 1800,
        ShiftCeilingRpm = 3800,
        DownshiftRpm = 1100,
        AirFuelRatio = 14.5,
        FuelDensityGramsPerLitre = 832,
        StoredDtcs = ["P0401", "P2002"],
        PendingDtcs = ["P0299"],
        FreezeFrameDtc = "P2002"
    };

    /// <summary>A heavy diesel pickup: three and a half tonnes, 6.7 litres, and a 117 litre tank.</summary>
    public static EmulatedVehicle Ram2500 { get; } = new()
    {
        Name = "2018 Ram 2500 SLT (Cummins)",
        Vin = "3C6UR5DL7JG264913",
        Decodes = "2018 Ram 2500 SLT · 6.7L I6 diesel · 4WD · Pickup",
        Note = "Heavy diesel. Mass air flow and fuel rate are an order of magnitude off the cars - useful for gauge scaling.",
        FuelTypeCode = 4,
        CompressionIgnition = true,
        EcuName = "CM2350-Cummins",
        CalibrationId = "68437280AA",
        UnsupportedPids = [.. DieselDoesNotHave],
        MassKg = 3400,
        DisplacementLitres = 6.7,
        PeakPowerWatts = 276_000,
        PeakTractiveForceN = 14_000,
        IdleRpm = 700,
        TankLitres = 117,
        GearRatios = [118, 70, 46, 33, 25, 20],
        ShiftFloorRpm = 1700,
        ShiftCeilingRpm = 2900,
        DownshiftRpm = 1050,
        AirFuelRatio = 14.5,
        FuelDensityGramsPerLitre = 832,
        StoredDtcs = ["P20EE", "P2459"],
        PendingDtcs = ["P0087"],
        FreezeFrameDtc = "P20EE"
    };

    /// <summary>
    /// Battery electric: no engine, and a third of the mode 01 PIDs simply gone. The one that breaks
    /// apps.
    /// </summary>
    public static EmulatedVehicle NissanLeaf { get; } = new()
    {
        Name = "2019 Nissan Leaf",
        Vin = "1N4AZ1CP9KC310468",
        Decodes = "2019 Nissan Leaf · BEV · Hatchback",
        Note = "No rpm, no fuel level, no air flow, no O2. A real Leaf answers most of what it knows on manufacturer-specific PIDs, which is out of scope for OBD-II.",
        FuelTypeCode = 8,
        HybridBatteryPercent = 78,
        IsElectric = true,
        EcuName = "VCM-EV",
        CalibrationId = "284B2-5SA0A",
        UnsupportedPids = [.. ElectricDoesNotHave],
        MassKg = 1580,
        DisplacementLitres = 0,
        PeakPowerWatts = 110_000,
        PeakTractiveForceN = 9_000,
        IdleRpm = 0,
        TankLitres = 0,

        // Single-speed reduction gear: the motor turns about 69 rpm for every km/h.
        GearRatios = [69],
        StoredDtcs = ["P0A80"],
        PendingDtcs = ["P0AA6"],
        FreezeFrameDtc = "P0A80"
    };

    /// <summary>
    /// Early OBD-II: no mode 09 at all, so there is no VIN to read off the bus. Every app that keys a
    /// vehicle by VIN needs an answer for this one.
    /// </summary>
    public static EmulatedVehicle ChevroletCavalier { get; } = new()
    {
        Name = "1998 Chevrolet Cavalier",
        Vin = "1G1JC5246W7180425",
        Decodes = "1998 Chevrolet Cavalier · 2.2L I4 · Sedan",
        Note = "Answers no mode 09, so mode 09 PID 02 returns NO DATA and the VIN above is unreachable over the bus - the case an app that keys off VIN has to survive. Mode 09 only became universal from about 2005.",
        ObdStandard = 1,
        ReportsVehicleInformation = false,
        EcuName = "PCM-16238212",
        CalibrationId = "16238212",
        UnsupportedPids = [0x22, 0x23, 0x2E, 0x32, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4A, 0x4B, 0x4C, 0x4D, 0x4E, 0x51, 0x52, 0x53, 0x54, 0x59, 0x5A, 0x5B, 0x5C, 0x5D, 0x5E, 0x61, 0x62, 0x63, 0x64, 0x7F, 0xA6],
        MassKg = 1200,
        DisplacementLitres = 2.2,
        PeakPowerWatts = 86_000,
        PeakTractiveForceN = 4_400,
        IdleRpm = 800,
        TankLitres = 53,
        GearRatios = [85, 48, 30],
        ShiftFloorRpm = 2200,
        ShiftCeilingRpm = 4400,
        StoredDtcs = ["P0300", "P0420"],
        PendingDtcs = [],
        FreezeFrameDtc = "P0300"
    };

    /// <summary>Every vehicle in the catalog, in a sensible order to offer them.</summary>
    public static IReadOnlyList<EmulatedVehicle> All { get; } =
    [
        HondaCivic,
        ToyotaCamry,
        Bmw330i,
        ToyotaPrius,
        VolkswagenGolfTdi,
        Ram2500,
        NissanLeaf,
        ChevroletCavalier
    ];

    /// <summary>What the emulator is until you pick something else.</summary>
    public static EmulatedVehicle Default => HondaCivic;

    /// <summary>
    /// Narrowband O2 voltage and lambda across all eight sensor positions, the evaporative purge and
    /// vapour pressures, and the ethanol content - a compression-ignition engine has none of them.
    /// </summary>
    static IEnumerable<byte> DieselDoesNotHave =>
    [
        0x2E, 0x32, 0x52, 0x53, 0x54,
        .. Range(0x14, 0x1B),   // O2 sensor voltage, B1S1 to B2S4
        .. Range(0x24, 0x2B),   // O2 lambda with voltage
        .. Range(0x34, 0x3B)    // O2 lambda with current
    ];

    /// <summary>
    /// Everything that presumes a combustion engine: rpm, air flow, fuel supply and metering, spark
    /// timing, the evaporative and EGR systems, and engine oil.
    /// </summary>
    static IEnumerable<byte> ElectricDoesNotHave =>
    [
        0x03, 0x04, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0E,
        0x10, 0x1F, 0x22, 0x23, 0x2C, 0x2D, 0x2E, 0x2F, 0x30, 0x32,
        0x43, 0x44, 0x52, 0x53, 0x54, 0x59, 0x5C, 0x5D, 0x5E, 0x63, 0x64, 0x7F,
        .. Range(0x14, 0x1B),
        .. Range(0x24, 0x2B),
        .. Range(0x34, 0x3B)
    ];

    static IEnumerable<byte> Range(byte first, byte last)
    {
        for (var pid = first; pid <= last; pid++)
            yield return pid;
    }
}
