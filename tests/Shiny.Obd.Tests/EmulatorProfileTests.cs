using System.Text;
using System.Text.Json;
using Json.Schema;
using Shiny.Obd.Commands;
using Shiny.Obd.Emulator;
using Shiny.Obd.Wifi;

namespace Shiny.Obd.Tests;

/// <summary>
/// The profile format is a contract with files living in other repos, so these hold the loader and the
/// published <c>obd-emulator.schema.json</c> to the same rules: what one accepts the other accepts,
/// and every built-in vehicle and scenario can be written out as a file that validates.
/// </summary>
public class EmulatorProfileTests
{
    static readonly JsonSchema Schema = JsonSchema.FromFile(Path.Combine(AppContext.BaseDirectory, "obd-emulator.schema.json"));

    const string FullProfile = """
    {
      "$schema": "https://raw.githubusercontent.com/shinyorg/obd/main/obd-emulator.schema.json",
      "formatVersion": 1,
      "vehicle": {
        "name": "2017 Ford F-150 XLT",
        "vin": "1FTEW1EG5HFA12345",
        "decodes": "2017 Ford F-150 XLT · 3.5L V6 EcoBoost",
        "note": "Twin turbo - absolute load well past 100%",
        "fuelTypeCode": 1,
        "obdStandard": 1,
        "ecuName": "PCM-EngineControl",
        "calibrationId": "HL3A-12A650-AXA",
        "unsupportedPids": ["5B", "2F"],
        "storedDtcs": ["P0299"],
        "pendingDtcs": [],
        "freezeFrameDtc": "P0299",
        "massKg": 2200,
        "displacementLitres": 3.5,
        "peakPowerWatts": 280000,
        "peakTractiveForceN": 9000,
        "idleRpm": 600,
        "tankLitres": 98,
        "gearRatios": [120, 75, 52, 40, 32, 26, 22, 19, 17, 15],
        "shiftCeilingRpm": 5500
      },
      "scenarios": [
        {
          "name": "Tow uphill",
          "summary": "Heavy load, low speed",
          "loops": false,
          "steps": [
            { "label": "Pull away", "action": "accelerate", "targetSpeedKph": 60, "seconds": 20 },
            { "label": "Climb", "action": "cruise", "targetSpeedKph": 60, "seconds": 120 },
            { "label": "Panic stop", "action": "harshBrake", "targetSpeedKph": 0, "seconds": 5 }
          ]
        }
      ],
      "state": {
        "adapterIdentifier": "STN2120 v5.6.19",
        "monitorsComplete": true,
        "permanentDtcs": ["P0420"],
        "parameters": [
          { "mode": "01", "pid": "0D", "number": 88 },
          { "mode": "01", "pid": "03", "hex": "01 00" },
          { "mode": "01", "pid": "05", "supported": false },
          { "mode": "09", "pid": "0A", "text": "PCM-Override" }
        ]
      }
    }
    """;


    [Fact]
    public void FullProfile_MatchesTheSchema() => AssertValid(FullProfile);


    [Fact]
    public void Parse_ReadsEverySection()
    {
        var profile = ObdEmulatorProfile.Parse(FullProfile);

        var vehicle = profile.Vehicle!;
        Assert.Equal("1FTEW1EG5HFA12345", vehicle.Vin);
        Assert.Equal([0x5B, 0x2F], vehicle.UnsupportedPids);
        Assert.Equal(10, vehicle.GearRatios.Count);
        Assert.Equal(5500, vehicle.ShiftCeilingRpm);
        Assert.Equal(1250, vehicle.DownshiftRpm);   // omitted - keeps the EmulatedVehicle default
        Assert.True(vehicle.ReportsVehicleInformation);

        var scenario = Assert.Single(profile.Scenarios!);
        Assert.False(scenario.Loops);
        Assert.Equal(DrivingAction.HarshBrake, scenario.Steps[2].Action);

        Assert.Equal("STN2120 v5.6.19", profile.State!.AdapterIdentifier);
        Assert.Null(profile.State.MilOn);
        Assert.Equal(4, profile.State.Parameters!.Count);
        Assert.Equal(0x0D, profile.State.Parameters[0].Pid);
    }


    [Fact]
    public void ApplyTo_SelectsTheVehicle_ThenAppliesTheState()
    {
        var state = new ObdEmulatorState();
        var player = new DrivingScenarioPlayer(state, new SynchronizationContextDispatcher(null));

        ObdEmulatorProfile.Parse(FullProfile).ApplyTo(state, player);

        Assert.Equal("2017 Ford F-150 XLT", state.Vehicle.Name);
        Assert.Contains(state.Vehicles, x => x.Name == "2017 Ford F-150 XLT");
        Assert.False(state.Find(0x01, 0x5B)!.IsSupported);
        Assert.Equal(["P0299"], state.StoredDtcs);
        Assert.Equal(["P0420"], state.PermanentDtcs);
        Assert.True(state.MilOn);                          // from the vehicle's stored code - state left it alone
        Assert.True(state.MonitorsComplete);
        Assert.Equal("STN2120 v5.6.19", state.AdapterIdentifier);

        Assert.Equal(88, state.Find(0x01, 0x0D)!.Number);
        Assert.Equal("01 00", state.Find(0x01, 0x03)!.Hex);
        Assert.False(state.Find(0x01, 0x05)!.IsSupported);
        Assert.Equal("PCM-Override", state.Find(0x09, 0x0A)!.Text);   // state wins over the vehicle's ecuName

        Assert.Contains(player.Scenarios, x => x.Name == "Tow uphill");
    }


    [Fact]
    public void ApplyTo_ReplacesScenariosAndVehiclesWithTheSameName()
    {
        var state = new ObdEmulatorState();
        var player = new DrivingScenarioPlayer(state, new SynchronizationContextDispatcher(null));
        var vehicleCount = state.Vehicles.Count;
        var scenarioCount = player.Scenarios.Count;

        var json = $$"""
        {
          "vehicle": {{Serialize(VehicleCatalog.HondaCivic with { IdleRpm = 999 })}},
          "scenarios": [ { "name": "Warm idle", "summary": "Shorter", "steps": [ { "label": "Idle", "action": "idle", "targetSpeedKph": 0, "seconds": 10 } ] } ]
        }
        """;
        ObdEmulatorProfile.Parse(json).ApplyTo(state, player);

        Assert.Equal(vehicleCount, state.Vehicles.Count);
        Assert.Equal(999, state.Vehicles.Single(x => x.Name == VehicleCatalog.HondaCivic.Name).IdleRpm);
        Assert.Equal(scenarioCount, player.Scenarios.Count);
        Assert.Equal("Shorter", player.Scenarios.Single(x => x.Name == "Warm idle").Summary);
    }


    [Fact]
    public void ApplyTo_SameVehicleTwice_ResetsTheFaultMemory()
    {
        var state = new ObdEmulatorState();
        var profile = new ObdEmulatorProfile { Vehicle = VehicleCatalog.ToyotaCamry };

        profile.ApplyTo(state);
        state.ClearDtcs();
        profile.ApplyTo(state);

        Assert.Equal(VehicleCatalog.ToyotaCamry.StoredDtcs, state.StoredDtcs);
    }


    [Fact]
    public void ApplyTo_StateOnly_LeavesTheVehicleAlone()
    {
        var state = new ObdEmulatorState { Vehicle = VehicleCatalog.NissanLeaf };

        ObdEmulatorProfile
            .Parse("""{ "state": { "storedDtcs": [], "milOn": false, "engineOff": true } }""")
            .ApplyTo(state);

        Assert.Same(VehicleCatalog.NissanLeaf, state.Vehicle);
        Assert.Empty(state.StoredDtcs);
        Assert.False(state.MilOn);
        Assert.True(state.EngineOff);
    }


    public static TheoryData<string> CatalogVehicles => [.. VehicleCatalog.All.Select(x => x.Name)];

    [Theory]
    [MemberData(nameof(CatalogVehicles))]
    public void CatalogVehicle_WritesAFileTheSchemaAccepts_AndReadsItBack(string name)
    {
        var profile = new ObdEmulatorProfile
        {
            Vehicle = VehicleCatalog.All.Single(x => x.Name == name),
            Scenarios = DrivingScenarioCatalog.All
        };

        var json = profile.ToJson();
        AssertValid(json);
        Assert.Equal(json, ObdEmulatorProfile.Parse(json).ToJson());
    }


    [Fact]
    public void Capture_RoundTripsTheEmulator()
    {
        var source = new ObdEmulatorState { Vehicle = VehicleCatalog.Bmw330i };
        source.AdapterIdentifier = "OBDLink MX+ STN2255";
        source.Find(0x01, 0x0D)!.Number = 123;
        source.Find(0x01, 0x2F)!.IsSupported = false;
        source.Find(0x01, 0x0C)!.RawOverride = "FF";
        source.PendingDtcs.Add("U0100");
        source.MonitorsComplete = true;
        var custom = new DrivingScenario
        {
            Name = "Lap",
            Summary = "One lap",
            Steps = [new("Go", DrivingAction.Accelerate, 100, 10)]
        };

        var json = ObdEmulatorProfile.Capture(source, [custom]).ToJson();
        AssertValid(json);

        var target = new ObdEmulatorState();
        var player = new DrivingScenarioPlayer(target, new SynchronizationContextDispatcher(null));
        ObdEmulatorProfile.Parse(json).ApplyTo(target, player);

        Assert.Equal(VehicleCatalog.Bmw330i.Name, target.Vehicle.Name);
        Assert.Equal(VehicleCatalog.Bmw330i.Vin, target.Vehicle.Vin);
        Assert.Equal("OBDLink MX+ STN2255", target.AdapterIdentifier);
        Assert.Equal(123, target.Find(0x01, 0x0D)!.Number);
        Assert.False(target.Find(0x01, 0x2F)!.IsSupported);
        Assert.Equal("FF", target.Find(0x01, 0x0C)!.RawOverride);
        Assert.Equal(source.PendingDtcs, target.PendingDtcs);
        Assert.True(target.MonitorsComplete);
        Assert.Contains(player.Scenarios, x => x.Name == "Lap");
    }


    [Fact]
    public void Capture_OnlyCarriesParametersThatChanged()
    {
        var state = new ObdEmulatorState();
        state.Find(0x01, 0x0D)!.Number = 42;

        var parameter = Assert.Single(ObdEmulatorProfile.Capture(state).State!.Parameters!);
        Assert.Equal(0x0D, parameter.Pid);
        Assert.Equal(42, parameter.Number);
        Assert.Null(parameter.Supported);
    }


    [Fact]
    public void Capture_WritesANullFreezeFrame_SoItDoesNotLoadBackAsTheDefault()
    {
        var state = new ObdEmulatorState { Vehicle = VehicleCatalog.HondaCivic with { Name = "Clean", FreezeFrameDtc = null } };

        var json = ObdEmulatorProfile.Capture(state).ToJson();

        Assert.Contains("\"freezeFrameDtc\": null", json);
        Assert.Null(ObdEmulatorProfile.Parse(json).Vehicle!.FreezeFrameDtc);
    }


    [Fact]
    public async Task Load_ReadsAStream_AndSaveWritesOneBack()
    {
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(FullProfile));
        var profile = await ObdEmulatorProfile.Load(input);

        await using var output = new MemoryStream();
        await profile.Save(output);

        var json = Encoding.UTF8.GetString(output.ToArray());
        AssertValid(json);
        Assert.Equal(profile.ToJson(), json);
    }


    [Fact]
    public async Task AppliedProfile_IsWhatAClientReadsOverTheWire()
    {
        var config = new ObdEmulatorConfiguration { TcpPort = 0 };
        var state = new ObdEmulatorState();
        var server = new TcpObdServer(new Elm327Responder(state), config);
        var host = new ObdEmulatorHost([server], config, new SynchronizationContextDispatcher(null));

        ObdEmulatorProfile.Parse(FullProfile).ApplyTo(state);
        await host.Start();
        try
        {
            await using var connection = new ObdConnection(new WifiObdTransport("127.0.0.1", server.BoundPort));
            await connection.Connect();

            Assert.Equal(88, await connection.Execute(StandardCommands.VehicleSpeed));
            Assert.Equal("1FTEW1EG5HFA12345", await connection.Execute(StandardCommands.Vin));
            Assert.Equal(ObdAdapterType.ObdLink, connection.DetectedAdapter!.Type);
            await Assert.ThrowsAnyAsync<ObdException>(() => connection.Execute(StandardCommands.CoolantTemperature));
        }
        finally
        {
            await host.Stop();
        }
    }


    // Documents both the schema and the loader turn away - the two must agree on what a valid file is.
    public static TheoryData<string, string> RejectedByBoth => new()
    {
        { "unknown property", """{ "vehical": {} }""" },
        { "newer format", """{ "formatVersion": 2 }""" },
        { "vehicle missing name", Vehicle("\"name\": \"2017 Ford F-150 XLT\",", "") },
        { "implausible VIN", Vehicle("1FTEW1EG5HFA12345", "1FTEW1EG5HFA1234O") },
        { "unencodable stored DTC", Vehicle("\"P0299\"]", "\"P4299\"]") },
        { "PID not hex", Vehicle("\"5B\", \"2F\"", "\"5B\", \"ZZ\"") },
        { "PID as number", Vehicle("\"5B\", \"2F\"", "91") },
        { "misspelt vehicle field", Vehicle("\"tankLitres\"", "\"tankLiters\"") },
        { "unknown action", """{ "scenarios": [ { "name": "x", "summary": "x", "steps": [ { "label": "x", "action": "reverse", "targetSpeedKph": 0, "seconds": 1 } ] } ] }""" },
        { "zero length step", """{ "scenarios": [ { "name": "x", "summary": "x", "steps": [ { "label": "x", "action": "idle", "targetSpeedKph": 0, "seconds": 0 } ] } ] }""" },
        { "no steps", """{ "scenarios": [ { "name": "x", "summary": "x", "steps": [] } ] }""" },
        { "bad state DTC", """{ "state": { "pendingDtcs": ["X0100"] } }""" }
    };

    [Theory]
    [MemberData(nameof(RejectedByBoth))]
    public void Invalid_IsRejectedByTheSchemaAndTheLoader(string reason, string json)
    {
        Assert.False(Evaluate(json).IsValid, $"schema accepted: {reason}");
        Assert.Throws<ObdException>(() => ObdEmulatorProfile.Parse(json));
    }


    // The schema cannot know the emulator's PID catalog, so these are the loader's alone - and they must
    // fail before anything is applied.
    [Theory]
    [InlineData("""{ "mode": "01", "pid": "FF", "number": 1 }""")]
    [InlineData("""{ "mode": "01", "pid": "0D", "text": "fast" }""")]
    [InlineData("""{ "mode": "09", "pid": "02", "number": 1 }""")]
    public void ParameterTheEmulatorCannotTake_ThrowsAndChangesNothing(string parameter)
    {
        var json = $$"""
        {
          "vehicle": {{Serialize(VehicleCatalog.NissanLeaf)}},
          "state": { "milOn": false, "parameters": [ {{parameter}} ] }
        }
        """;
        AssertValid(json);

        var state = new ObdEmulatorState();
        var profile = ObdEmulatorProfile.Parse(json);

        Assert.Throws<ObdException>(() => profile.ApplyTo(state));
        Assert.Same(VehicleCatalog.Default, state.Vehicle);
        Assert.True(state.MilOn);
    }


    static string Vehicle(string find, string replace)
    {
        var json = FullProfile.Replace(find, replace);
        Assert.NotEqual(FullProfile, json);
        return json;
    }


    static string Serialize(EmulatedVehicle vehicle)
    {
        var json = new ObdEmulatorProfile { Vehicle = vehicle }.ToJson();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("vehicle").GetRawText();
    }


    static EvaluationResults Evaluate(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Schema.Evaluate(doc.RootElement.Clone(), new EvaluationOptions { OutputFormat = OutputFormat.List });
    }


    static void AssertValid(string json)
    {
        var result = Evaluate(json);
        var errors = (result.Details ?? [])
            .Where(x => x.Errors != null)
            .SelectMany(x => x.Errors!.Select(e => $"{x.InstanceLocation}: {e.Value}"));

        Assert.True(result.IsValid, String.Join(Environment.NewLine, errors));
    }
}
