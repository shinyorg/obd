using System.Text.Json.Serialization;

namespace Shiny.Obd.Emulator;

// The wire shape of a profile. These exist because the JSON source generator does not keep a property
// initializer's value for an init-only property that is missing from the document - it writes
// default(T) instead - so EmulatedVehicle read directly comes back with a 0 kg mass and a 0 rpm
// downshift for every field a file leaves out. Settable properties seeded from the domain types' own
// defaults keep "omitted" meaning "the EmulatedVehicle default" without restating any of the numbers.

sealed class ProfileDocument
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    public int FormatVersion { get; set; } = ObdEmulatorProfile.CurrentFormatVersion;
    public VehicleDocument? Vehicle { get; set; }
    public List<ScenarioDocument>? Scenarios { get; set; }
    public ObdEmulatorStateSnapshot? State { get; set; }
}


sealed class VehicleDocument
{
    static readonly EmulatedVehicle Defaults = new()
    {
        Name = "",
        Vin = "",
        Decodes = "",
        Note = "",
        EcuName = "",
        CalibrationId = ""
    };

    [JsonRequired] public string Name { get; set; } = "";
    [JsonRequired] public string Vin { get; set; } = "";
    [JsonRequired] public string Decodes { get; set; } = "";
    [JsonRequired] public string Note { get; set; } = "";

    public byte FuelTypeCode { get; set; } = Defaults.FuelTypeCode;
    public byte ObdStandard { get; set; } = Defaults.ObdStandard;
    public double EthanolPercent { get; set; } = Defaults.EthanolPercent;
    public double HybridBatteryPercent { get; set; } = Defaults.HybridBatteryPercent;
    public bool CompressionIgnition { get; set; } = Defaults.CompressionIgnition;
    [JsonRequired] public string EcuName { get; set; } = "";
    [JsonRequired] public string CalibrationId { get; set; } = "";

    [JsonConverter(typeof(HexByteListJsonConverter))]
    public List<byte> UnsupportedPids { get; set; } = [.. Defaults.UnsupportedPids];

    public bool ReportsVehicleInformation { get; set; } = Defaults.ReportsVehicleInformation;
    public List<string> StoredDtcs { get; set; } = [.. Defaults.StoredDtcs];
    public List<string> PendingDtcs { get; set; } = [.. Defaults.PendingDtcs];

    // null means "no freeze frame stored", so it is written even when null - leaving it out would load
    // back as the default code
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? FreezeFrameDtc { get; set; } = Defaults.FreezeFrameDtc;

    public double MassKg { get; set; } = Defaults.MassKg;
    public double DisplacementLitres { get; set; } = Defaults.DisplacementLitres;
    public double PeakPowerWatts { get; set; } = Defaults.PeakPowerWatts;
    public double PeakTractiveForceN { get; set; } = Defaults.PeakTractiveForceN;
    public double IdleRpm { get; set; } = Defaults.IdleRpm;
    public double TankLitres { get; set; } = Defaults.TankLitres;
    public List<double> GearRatios { get; set; } = [.. Defaults.GearRatios];
    public double ShiftFloorRpm { get; set; } = Defaults.ShiftFloorRpm;
    public double ShiftCeilingRpm { get; set; } = Defaults.ShiftCeilingRpm;
    public double DownshiftRpm { get; set; } = Defaults.DownshiftRpm;
    public double AirFuelRatio { get; set; } = Defaults.AirFuelRatio;
    public double FuelDensityGramsPerLitre { get; set; } = Defaults.FuelDensityGramsPerLitre;
    public bool IsElectric { get; set; } = Defaults.IsElectric;


    public EmulatedVehicle ToVehicle() => new()
    {
        Name = this.Name,
        Vin = this.Vin,
        Decodes = this.Decodes,
        Note = this.Note,
        FuelTypeCode = this.FuelTypeCode,
        ObdStandard = this.ObdStandard,
        EthanolPercent = this.EthanolPercent,
        HybridBatteryPercent = this.HybridBatteryPercent,
        CompressionIgnition = this.CompressionIgnition,
        EcuName = this.EcuName,
        CalibrationId = this.CalibrationId,
        UnsupportedPids = this.UnsupportedPids,
        ReportsVehicleInformation = this.ReportsVehicleInformation,
        StoredDtcs = this.StoredDtcs.Select(x => x.ToUpperInvariant()).ToList(),
        PendingDtcs = this.PendingDtcs.Select(x => x.ToUpperInvariant()).ToList(),
        FreezeFrameDtc = this.FreezeFrameDtc?.ToUpperInvariant(),
        MassKg = this.MassKg,
        DisplacementLitres = this.DisplacementLitres,
        PeakPowerWatts = this.PeakPowerWatts,
        PeakTractiveForceN = this.PeakTractiveForceN,
        IdleRpm = this.IdleRpm,
        TankLitres = this.TankLitres,
        GearRatios = this.GearRatios,
        ShiftFloorRpm = this.ShiftFloorRpm,
        ShiftCeilingRpm = this.ShiftCeilingRpm,
        DownshiftRpm = this.DownshiftRpm,
        AirFuelRatio = this.AirFuelRatio,
        FuelDensityGramsPerLitre = this.FuelDensityGramsPerLitre,
        IsElectric = this.IsElectric
    };


    public static VehicleDocument From(EmulatedVehicle vehicle) => new()
    {
        Name = vehicle.Name,
        Vin = vehicle.Vin,
        Decodes = vehicle.Decodes,
        Note = vehicle.Note,
        FuelTypeCode = vehicle.FuelTypeCode,
        ObdStandard = vehicle.ObdStandard,
        EthanolPercent = vehicle.EthanolPercent,
        HybridBatteryPercent = vehicle.HybridBatteryPercent,
        CompressionIgnition = vehicle.CompressionIgnition,
        EcuName = vehicle.EcuName,
        CalibrationId = vehicle.CalibrationId,
        UnsupportedPids = [.. vehicle.UnsupportedPids],
        ReportsVehicleInformation = vehicle.ReportsVehicleInformation,
        StoredDtcs = [.. vehicle.StoredDtcs],
        PendingDtcs = [.. vehicle.PendingDtcs],
        FreezeFrameDtc = vehicle.FreezeFrameDtc,
        MassKg = vehicle.MassKg,
        DisplacementLitres = vehicle.DisplacementLitres,
        PeakPowerWatts = vehicle.PeakPowerWatts,
        PeakTractiveForceN = vehicle.PeakTractiveForceN,
        IdleRpm = vehicle.IdleRpm,
        TankLitres = vehicle.TankLitres,
        GearRatios = [.. vehicle.GearRatios],
        ShiftFloorRpm = vehicle.ShiftFloorRpm,
        ShiftCeilingRpm = vehicle.ShiftCeilingRpm,
        DownshiftRpm = vehicle.DownshiftRpm,
        AirFuelRatio = vehicle.AirFuelRatio,
        FuelDensityGramsPerLitre = vehicle.FuelDensityGramsPerLitre,
        IsElectric = vehicle.IsElectric
    };
}


sealed class ScenarioDocument
{
    [JsonRequired] public string Name { get; set; } = "";
    [JsonRequired] public string Summary { get; set; } = "";
    public bool Loops { get; set; } = true;
    [JsonRequired] public List<StepDocument> Steps { get; set; } = [];

    public DrivingScenario ToScenario() => new()
    {
        Name = this.Name,
        Summary = this.Summary,
        Loops = this.Loops,
        Steps = this.Steps.Select(x => new DrivingStep(x.Label, x.Action, x.TargetSpeedKph, x.Seconds)).ToList()
    };

    public static ScenarioDocument From(DrivingScenario scenario) => new()
    {
        Name = scenario.Name,
        Summary = scenario.Summary,
        Loops = scenario.Loops,
        Steps = scenario.Steps.Select(x => new StepDocument
        {
            Label = x.Label,
            Action = x.Action,
            TargetSpeedKph = x.TargetSpeedKph,
            Seconds = x.Seconds
        }).ToList()
    };
}


sealed class StepDocument
{
    [JsonRequired] public string Label { get; set; } = "";
    [JsonRequired] public DrivingAction Action { get; set; }
    [JsonRequired] public double TargetSpeedKph { get; set; }
    [JsonRequired] public double Seconds { get; set; }
}
