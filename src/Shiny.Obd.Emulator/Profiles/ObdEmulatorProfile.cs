using System.Collections.ObjectModel;
using System.Text.Json;
using Shiny.Obd.Vin;

namespace Shiny.Obd.Emulator;

/// <summary>
/// A vehicle, driving scenarios and starting state in one JSON document, so an emulator setup can be
/// shared between projects instead of rebuilt in each one's code.
/// </summary>
/// <remarks>
/// <para>
/// The format is described by <c>obd-emulator.schema.json</c> at the root of the repo
/// (<see cref="SchemaUrl"/>). Every section is optional: a file can carry just a vehicle, just a
/// scenario, or just a fault state to put on top of whichever vehicle is already selected.
/// </para>
/// <para>
/// Everything is validated before anything is applied - an unknown PID, an unencodable trouble code
/// or a value of the wrong kind throws <see cref="ObdException"/> and leaves the emulator untouched,
/// rather than half-applying a file whose author made a typo.
/// </para>
/// </remarks>
public sealed class ObdEmulatorProfile
{
    /// <summary>The newest format this version of the emulator reads.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Where the published schema lives. Written into saved files so editors can validate them.</summary>
    public const string SchemaUrl = "https://raw.githubusercontent.com/shinyorg/obd/main/obd-emulator.schema.json";

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    /// <summary>Selected when the profile is applied - which rewrites identity PIDs, support and fault memory.</summary>
    public EmulatedVehicle? Vehicle { get; init; }

    /// <summary>Added to the scenario player. One with the same name as an existing scenario replaces it.</summary>
    public IReadOnlyList<DrivingScenario>? Scenarios { get; init; }

    /// <summary>Applied after <see cref="Vehicle"/>, so it can override what the vehicle set.</summary>
    public ObdEmulatorStateSnapshot? State { get; init; }


    /// <summary>Reads a profile from JSON.</summary>
    /// <exception cref="ObdException">The JSON is malformed, does not match the format, or is a newer format version.</exception>
    public static ObdEmulatorProfile Parse(string json)
    {
        ProfileDocument? document;
        try
        {
            document = JsonSerializer.Deserialize(json, ObdEmulatorProfileJsonContext.Default.ProfileDocument);
        }
        catch (JsonException ex)
        {
            throw Invalid(ex);
        }
        return FromDocument(document);
    }


    /// <summary>Reads a profile from a stream - a file, an embedded resource, an HTTP response.</summary>
    /// <exception cref="ObdException">The JSON is malformed, does not match the format, or is a newer format version.</exception>
    public static async Task<ObdEmulatorProfile> Load(Stream stream, CancellationToken ct = default)
    {
        ProfileDocument? document;
        try
        {
            document = await JsonSerializer
                .DeserializeAsync(stream, ObdEmulatorProfileJsonContext.Default.ProfileDocument, ct)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw Invalid(ex);
        }
        return FromDocument(document);
    }


    /// <summary>Writes the profile as JSON, with <c>$schema</c> pointing at <see cref="SchemaUrl"/>.</summary>
    public string ToJson() => JsonSerializer.Serialize(this.ToDocument(), ObdEmulatorProfileJsonContext.Default.ProfileDocument);


    /// <summary>Writes the profile as JSON, with <c>$schema</c> pointing at <see cref="SchemaUrl"/>.</summary>
    public Task Save(Stream stream, CancellationToken ct = default)
        => JsonSerializer.SerializeAsync(stream, this.ToDocument(), ObdEmulatorProfileJsonContext.Default.ProfileDocument, ct);


    /// <summary>
    /// Snapshots the emulator so it can be saved and shared: the selected vehicle, the adapter and
    /// fault state, and every parameter that differs from what that vehicle starts with.
    /// </summary>
    /// <param name="state">The emulator to capture.</param>
    /// <param name="scenarios">Scenarios to include - typically your own, not the built-in catalog.</param>
    public static ObdEmulatorProfile Capture(ObdEmulatorState state, IEnumerable<DrivingScenario>? scenarios = null)
    {
        // A fresh state on the same vehicle is exactly what applying the vehicle alone would produce,
        // so anything that differs from it is what the profile has to carry.
        var baseline = new ObdEmulatorState { Vehicle = state.Vehicle };
        var parameters = state
            .Parameters
            .Select(x => Diff(x, baseline.Find(x.Mode, x.Pid)!))
            .OfType<ObdParameterSnapshot>()
            .ToList();

        var scenarioList = scenarios?.ToList();
        return new ObdEmulatorProfile
        {
            Vehicle = state.Vehicle,
            Scenarios = scenarioList is { Count: > 0 } ? scenarioList : null,
            State = new ObdEmulatorStateSnapshot
            {
                AdapterIdentifier = state.AdapterIdentifier,
                DeviceDescription = state.DeviceDescription,
                MilOn = state.MilOn,
                MonitorsComplete = state.MonitorsComplete,
                CompressionIgnition = state.CompressionIgnition,
                FreezeFrameCaptured = state.FreezeFrameCaptured,
                FreezeFrameDtc = String.IsNullOrWhiteSpace(state.FreezeFrameDtc) ? null : state.FreezeFrameDtc,
                EngineOff = state.EngineOff,
                StoredDtcs = [.. state.StoredDtcs],
                PendingDtcs = [.. state.PendingDtcs],
                PermanentDtcs = [.. state.PermanentDtcs],
                Parameters = parameters.Count > 0 ? parameters : null
            }
        };
    }


    /// <summary>
    /// Applies the profile: selects the vehicle, applies the state overrides on top of it, and adds the
    /// scenarios to <paramref name="player"/>.
    /// </summary>
    /// <remarks>
    /// Call it on the thread that owns any UI bound to the state - the same rule as editing the state
    /// directly. The vehicle is added to <see cref="ObdEmulatorState.Vehicles"/> (replacing one with the
    /// same name) so it stays selectable.
    /// </remarks>
    /// <exception cref="ObdException">A parameter is not one the emulator has, or a value does not fit its kind. Nothing is applied.</exception>
    public void ApplyTo(ObdEmulatorState state, DrivingScenarioPlayer? player = null)
    {
        // resolve everything first - a bad entry must not leave the emulator half-changed
        var parameters = (this.State?.Parameters ?? [])
            .Select(x => (Snapshot: x, Parameter: Resolve(state, x)))
            .ToList();

        if (this.Vehicle != null)
            SelectVehicle(state, this.Vehicle);

        if (this.State != null)
            ApplyState(state, this.State);

        foreach (var (snapshot, parameter) in parameters)
            ApplyParameter(parameter, snapshot);

        state.RefreshDerived();

        if (player != null && this.Scenarios != null)
        {
            foreach (var scenario in this.Scenarios)
                ReplaceByName(player.Scenarios, scenario, x => x.Name);
        }
    }


    static void SelectVehicle(ObdEmulatorState state, EmulatedVehicle vehicle)
    {
        ReplaceByName(state.Vehicles, vehicle, x => x.Name);

        // the setter only reapplies on a change, and loading the same file twice should still reset the car
        if (Equals(state.Vehicle, vehicle))
            state.ApplyVehicle();
        else
            state.Vehicle = vehicle;
    }


    static void ApplyState(ObdEmulatorState state, ObdEmulatorStateSnapshot snapshot)
    {
        if (snapshot.AdapterIdentifier != null)
            state.AdapterIdentifier = snapshot.AdapterIdentifier;

        if (snapshot.DeviceDescription != null)
            state.DeviceDescription = snapshot.DeviceDescription;

        if (snapshot.CompressionIgnition is { } compressionIgnition)
            state.CompressionIgnition = compressionIgnition;

        if (snapshot.StoredDtcs != null)
            Replace(state.StoredDtcs, snapshot.StoredDtcs);

        if (snapshot.PendingDtcs != null)
            Replace(state.PendingDtcs, snapshot.PendingDtcs);

        if (snapshot.PermanentDtcs != null)
            Replace(state.PermanentDtcs, snapshot.PermanentDtcs);

        // after the DTC lists, so a file that clears the codes but asks for the lamp still gets it
        if (snapshot.MilOn is { } milOn)
            state.MilOn = milOn;

        if (snapshot.MonitorsComplete is { } monitorsComplete)
            state.MonitorsComplete = monitorsComplete;

        if (snapshot.FreezeFrameCaptured is { } freezeFrameCaptured)
            state.FreezeFrameCaptured = freezeFrameCaptured;

        if (snapshot.FreezeFrameDtc != null)
            state.FreezeFrameDtc = snapshot.FreezeFrameDtc;

        if (snapshot.EngineOff is { } engineOff)
            state.EngineOff = engineOff;
    }


    static ObdParameter Resolve(ObdEmulatorState state, ObdParameterSnapshot snapshot)
    {
        var request = $"{snapshot.Mode:X2}{snapshot.Pid:X2}";
        var parameter = state.Find(snapshot.Mode, snapshot.Pid)
            ?? throw new ObdException($"The profile sets {request}, which the emulator has no parameter for");

        if (snapshot.Number != null && parameter.Kind != ObdValueKind.Number)
            throw WrongKind(request, parameter, "number");

        if (snapshot.Text != null && parameter.Kind != ObdValueKind.Text)
            throw WrongKind(request, parameter, "text");

        if (snapshot.Hex != null && parameter.Kind != ObdValueKind.Hex)
            throw WrongKind(request, parameter, "hex");

        if (!IsHexOrEmpty(snapshot.Hex))
            throw new ObdException($"The profile's hex for {request} ({parameter.Name}) is not whole hex pairs: '{snapshot.Hex}'");

        if (!IsHexOrEmpty(snapshot.RawOverride))
            throw new ObdException($"The profile's rawOverride for {request} ({parameter.Name}) is not whole hex pairs: '{snapshot.RawOverride}'");

        return parameter;
    }


    static void ApplyParameter(ObdParameter parameter, ObdParameterSnapshot snapshot)
    {
        if (snapshot.Supported is { } supported)
            parameter.IsSupported = supported;

        if (snapshot.Number is { } number)
            parameter.Number = number;

        if (snapshot.Text != null)
            parameter.Text = snapshot.Text;

        if (snapshot.Hex != null)
            parameter.Hex = snapshot.Hex;

        if (snapshot.RawOverride != null)
            parameter.RawOverride = snapshot.RawOverride;
    }


    static ObdParameterSnapshot? Diff(ObdParameter current, ObdParameter baseline)
    {
        // a derived parameter's value comes from elsewhere in the state, so only its switches can differ
        var derived = current.Kind == ObdValueKind.Derived;
        var snapshot = new ObdParameterSnapshot
        {
            Mode = current.Mode,
            Pid = current.Pid,
            Supported = current.IsSupported != baseline.IsSupported ? current.IsSupported : null,
            Number = !derived && current.Kind == ObdValueKind.Number && current.Number != baseline.Number ? current.Number : null,
            Text = !derived && current.Kind == ObdValueKind.Text && current.Text != baseline.Text ? current.Text : null,
            Hex = !derived && current.Kind == ObdValueKind.Hex && current.Hex != baseline.Hex ? current.Hex : null,
            RawOverride = current.RawOverride != baseline.RawOverride ? current.RawOverride : null
        };

        var changed = snapshot.Supported != null
            || snapshot.Number != null
            || snapshot.Text != null
            || snapshot.Hex != null
            || snapshot.RawOverride != null;

        return changed ? snapshot : null;
    }


    ProfileDocument ToDocument() => new()
    {
        Schema = SchemaUrl,
        FormatVersion = this.FormatVersion,
        Vehicle = this.Vehicle == null ? null : VehicleDocument.From(this.Vehicle),
        Scenarios = this.Scenarios?.Select(ScenarioDocument.From).ToList(),
        State = this.State
    };


    static ObdEmulatorProfile FromDocument(ProfileDocument? document)
    {
        if (document == null)
            throw new ObdException("Not a valid emulator profile: the document is empty or null");

        // checked before mapping - a newer file may use fields this version maps wrongly
        if (document.FormatVersion > CurrentFormatVersion)
            throw new ObdException($"This emulator profile is format version {document.FormatVersion}; this version of Shiny.Obd.Emulator reads up to {CurrentFormatVersion}. Update the package to load it.");

        if (document.FormatVersion < 1)
            throw new ObdException($"Not a valid emulator profile: formatVersion {document.FormatVersion} does not exist");

        return Validate(new ObdEmulatorProfile
        {
            FormatVersion = document.FormatVersion,
            Vehicle = document.Vehicle?.ToVehicle(),
            Scenarios = document.Scenarios?.Select(x => x.ToScenario()).ToList(),
            State = document.State
        });
    }


    static ObdEmulatorProfile Validate(ObdEmulatorProfile profile)
    {

        if (profile.Vehicle is { } vehicle)
        {
            if (!VinNumber.IsPlausible(vehicle.Vin))
                throw new ObdException($"Vehicle '{vehicle.Name}' has an implausible VIN '{vehicle.Vin}' - 17 characters, no I, O or Q. To test an app against a malformed VIN, set it as text on parameter 09 02 under state instead.");

            if (vehicle.GearRatios.Count == 0)
                throw new ObdException($"Vehicle '{vehicle.Name}' needs at least one gear ratio");

            ValidateDtcs($"vehicle '{vehicle.Name}' storedDtcs", vehicle.StoredDtcs);
            ValidateDtcs($"vehicle '{vehicle.Name}' pendingDtcs", vehicle.PendingDtcs);
            if (vehicle.FreezeFrameDtc != null)
                ValidateDtcs($"vehicle '{vehicle.Name}' freezeFrameDtc", [vehicle.FreezeFrameDtc]);
        }

        foreach (var scenario in profile.Scenarios ?? [])
        {
            if (scenario.Steps.Count == 0)
                throw new ObdException($"Scenario '{scenario.Name}' has no steps");

            var bad = scenario.Steps.FirstOrDefault(x => x.Seconds <= 0 || x.TargetSpeedKph < 0);
            if (bad != null)
                throw new ObdException($"Scenario '{scenario.Name}' step '{bad.Label}' needs a positive duration and a non-negative target speed");
        }

        if (profile.State is { } state)
        {
            ValidateDtcs("state storedDtcs", state.StoredDtcs ?? []);
            ValidateDtcs("state pendingDtcs", state.PendingDtcs ?? []);
            ValidateDtcs("state permanentDtcs", state.PermanentDtcs ?? []);
            if (state.FreezeFrameDtc != null)
                ValidateDtcs("state freezeFrameDtc", [state.FreezeFrameDtc]);
        }

        return profile;
    }


    static void ValidateDtcs(string where, IReadOnlyList<string> codes)
    {
        var bad = codes.FirstOrDefault(x => !ObdEmulatorState.TryEncodeDtc(x, out _, out _));
        if (bad != null)
            throw new ObdException($"'{bad}' in {where} is not an encodable trouble code - expected a letter P/C/B/U, then 0-3, then three hex digits, e.g. P0301");
    }


    static void ReplaceByName<T>(IList<T> list, T item, Func<T, string> name)
    {
        var index = list
            .Select((x, i) => (Item: x, Index: i))
            .Where(x => name(x.Item) == name(item))
            .Select(x => x.Index)
            .DefaultIfEmpty(-1)
            .First();

        if (index >= 0)
            list[index] = item;
        else
            list.Add(item);
    }


    static void Replace(ObservableCollection<string> target, IReadOnlyList<string> values)
    {
        target.Clear();
        foreach (var value in values)
            target.Add(value.ToUpperInvariant());
    }


    static bool IsHexOrEmpty(string? value) => String.IsNullOrWhiteSpace(value) || ObdParameter.TryParseHex(value, out _);

    static ObdException WrongKind(string request, ObdParameter parameter, string field)
        => new($"The profile sets '{field}' on {request} ({parameter.Name}), which is a {parameter.Kind.ToString().ToLowerInvariant()} parameter");

    static ObdException Invalid(JsonException ex)
        => new($"Not a valid emulator profile: {ex.Message}", ex);
}
