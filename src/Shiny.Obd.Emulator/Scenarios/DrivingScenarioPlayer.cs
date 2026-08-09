namespace Shiny.Obd.Emulator;

/// <summary>
/// Plays a <see cref="DrivingScenario"/> into the emulator by writing the live mode 01 parameters five
/// times a second, so a connected client sees a vehicle being driven rather than a fixed set of values.
/// </summary>
/// <remarks>
/// <para>
/// There is one model of the car in here - speed, gear, throttle - and every parameter is derived from
/// it. That is what keeps the values consistent with each other: RPM matches the gear the speed implies,
/// mass air flow matches the load, and fuel rate matches the air flow. A client that cross-checks two
/// PIDs against each other will not catch the emulator contradicting itself.
/// </para>
/// <para>
/// The scenario supplies simulated time, not wall-clock time - each tick advances the drive by a fixed
/// <see cref="TickSeconds"/> whether or not the timer was late. A drive therefore plays the same way on
/// a busy device as on an idle one, at the cost of running slightly behind the clock under load.
/// </para>
/// </remarks>
public partial class DrivingScenarioPlayer(ObdEmulatorState state, IObdEmulatorDispatcher dispatcher) : ObservableObject
{
    const double TickSeconds = 0.2;

    CancellationTokenSource? cancel;

    /// <summary>
    /// Mass, displacement, power, gearing and fuel all come from <see cref="ObdEmulatorState.Vehicle"/>,
    /// so a drive in the Cummins pickup does not produce the same numbers as one in the Civic. Read
    /// fresh each tick - switching vehicle mid-drive takes effect on the next one.
    /// </summary>
    EmulatedVehicle Car => state.Vehicle;

    // ---- Vehicle model ----------------------------------------------------------------------------
    double speed;             // m/s
    double acceleration;      // m/s², as actually achieved last tick
    int gear;                 // index into GearRatios; -1 when stopped or with the clutch in
    double clock;             // seconds since the drive started, for the oscillators
    double coolantC;
    double oilC;
    double intakeC;
    double fuelPercent;
    double odometerKm;
    double clearedKm;
    double milKm;
    double runtimeSeconds;
    double clearedMinutes;
    double milMinutes;

    int stepIndex;
    double stepElapsed;

    /// <summary>A concrete list rather than the catalog's read-only view - a Picker binds to IList.</summary>
    public List<DrivingScenario> Scenarios { get; } = [.. DrivingScenarioCatalog.All];

    [ObservableProperty] DrivingScenario? scenario = DrivingScenarioCatalog.CityDriving;

    [ObservableProperty] bool isRunning;

    /// <summary>The step currently playing, so the live numbers have a caption.</summary>
    [ObservableProperty] string stepLabel = "Not driving";

    [ObservableProperty] string elapsedLabel = "0:00 / 0:00";

    /// <summary>How many times the scenario has looped. The reason this exists is unattended long runs.</summary>
    [ObservableProperty] int laps;

    // Read-outs. These mirror what was just written to the parameters - the UI binds here rather than
    // to the parameter list so a drive does not repaint every bound parameter five times a second.
    [ObservableProperty] double speedKph;
    [ObservableProperty] double rpm;
    [ObservableProperty] double throttlePercent;
    [ObservableProperty] double loadPercent;
    [ObservableProperty] double coolantTemperature;
    [ObservableProperty] double massAirFlow;
    [ObservableProperty] double fuelRate;
    [ObservableProperty] string gearLabel = "N";

    public string ScenarioSummary => this.Scenario?.Summary ?? "";

    public string ButtonText => this.IsRunning ? "Stop driving" : "Start driving";

    partial void OnIsRunningChanged(bool value) => this.OnPropertyChanged(nameof(this.ButtonText));

    partial void OnScenarioChanged(DrivingScenario? value)
    {
        this.OnPropertyChanged(nameof(this.ScenarioSummary));

        // Switching scenario mid-drive restarts at that scenario's first step rather than dropping into
        // it at whatever offset the old one had reached.
        if (this.IsRunning)
            this.Rewind();
    }

    public void Start()
    {
        if (this.IsRunning || this.Scenario == null)
            return;

        this.Seed();
        this.Rewind();

        var source = new CancellationTokenSource();
        this.cancel = source;
        this.IsRunning = true;

        _ = this.Run(source.Token);
    }

    public void Stop()
    {
        this.cancel?.Cancel();
        this.cancel?.Dispose();
        this.cancel = null;
        this.IsRunning = false;
        this.StepLabel = "Stopped - values are yours to edit again";
    }

    public void Toggle()
    {
        if (this.IsRunning)
            this.Stop();
        else
            this.Start();
    }

    async Task Run(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(TickSeconds));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                // Parameter writes raise PropertyChanged straight into bindings, so they belong on the
                // UI thread. The model is cheap enough that running all of it there costs nothing.
                dispatcher.Invoke(this.Advance);
            }
        }
        catch (OperationCanceledException)
        {
            // Stop() - nothing to report.
        }
    }

    /// <summary>Takes the current parameter values as the starting point, so a drive continues from wherever the vehicle was left.</summary>
    void Seed()
    {
        this.speed = 0;
        this.acceleration = 0;
        this.gear = -1;
        this.clock = 0;

        this.coolantC = this.Read(0x05, 88);
        this.oilC = this.Read(0x5C, 95);
        this.intakeC = this.Read(0x0F, 30);
        this.fuelPercent = this.Read(0x2F, 62);
        this.odometerKm = this.Read(0xA6, 84210.6);
        this.clearedKm = this.Read(0x31, 420);
        this.milKm = this.Read(0x21, 0);
        this.runtimeSeconds = this.Read(0x1F, 0);
        this.clearedMinutes = this.Read(0x4E, 2400);
        this.milMinutes = this.Read(0x4D, 0);
    }

    void Rewind()
    {
        this.stepIndex = 0;
        this.stepElapsed = 0;
        this.Laps = 0;
    }

    // ---- The drive ---------------------------------------------------------------------------------

    void Advance()
    {
        // A tick can already be queued for the UI thread when Stop lands; without this it would write one
        // more set of values - and its own caption - over the top of the stopped state.
        var scenario = this.Scenario;
        if (!this.IsRunning || scenario == null || scenario.Steps.Count == 0)
            return;

        var step = scenario.Steps[Math.Min(this.stepIndex, scenario.Steps.Count - 1)];

        this.clock += TickSeconds;
        this.stepElapsed += TickSeconds;

        this.Drive(step);
        this.Apply();
        this.Publish(scenario, step);

        if (this.stepElapsed < step.Seconds)
            return;

        this.stepElapsed = 0;
        this.stepIndex++;

        if (this.stepIndex < scenario.Steps.Count)
            return;

        if (!scenario.Loops)
        {
            this.Stop();
            this.StepLabel = "Drive finished";
            return;
        }

        this.stepIndex = 0;
        this.Laps++;
    }

    /// <summary>Moves the car for one tick: pick an acceleration from the step, integrate, then pick a gear.</summary>
    void Drive(DrivingStep step)
    {
        var target = step.TargetSpeedKph / 3.6;
        var previous = this.speed;

        var demand = step.Action switch
        {
            // A cruise is never truly steady - traffic breathes, and a value that never moves is the
            // one thing a real vehicle will not give you.
            DrivingAction.Cruise => Math.Clamp(0.5 * (target + (0.7 * Math.Sin(this.clock * 0.35)) - this.speed), -1.5, 1.5),
            DrivingAction.Accelerate => this.speed >= target ? 0 : Math.Min(this.AvailableAcceleration(), 0.9 * (target - this.speed)),
            DrivingAction.Coast => this.speed <= target ? 0 : -(0.45 + (0.012 * this.speed)),
            DrivingAction.Brake => this.speed <= target ? 0 : -2.8,
            DrivingAction.HarshBrake => this.speed <= target ? 0 : -7.0,
            _ => -3.0
        };

        this.speed = Math.Max(0, this.speed + (demand * TickSeconds));

        // Braking and coasting aim at a floor, accelerating at a ceiling; without the clamp a hard stop
        // overshoots into a phantom reverse and a hard launch sails past the limit.
        if (demand < 0 && step.Action != DrivingAction.Cruise)
            this.speed = Math.Max(this.speed, Math.Min(previous, target));
        else if (demand > 0 && step.Action == DrivingAction.Accelerate)
            this.speed = Math.Min(this.speed, target);

        this.acceleration = (this.speed - previous) / TickSeconds;
        this.SelectGear();
    }

    /// <summary>What the engine can pull at this speed - a car accelerates far harder at 20 km/h than at 120.</summary>
    double AvailableAcceleration()
    {
        var force = Math.Min(this.Car.PeakTractiveForceN, this.Car.PeakPowerWatts / Math.Max(this.speed, 3.0));
        return (force - this.RoadLoadNewtons()) / this.Car.MassKg;
    }

    double RoadLoadNewtons() => 220 + (0.42 * this.speed * this.speed);

    void SelectGear()
    {
        var ratios = this.Car.GearRatios;
        var kph = this.speed * 3.6;

        // A single-speed EV is always "in gear" - there is nothing to select and nothing to slip.
        if (kph < 4 && ratios.Count > 1)
        {
            this.gear = -1;
            return;
        }

        if (this.gear < 0)
            this.gear = 0;

        // Shift points move with load: a gentle pull-away shifts near the floor, a hard one hangs on to
        // the ceiling. That sawtooth in the RPM trace is the most recognisable thing about real data -
        // and a diesel's floor and ceiling are thousands of rpm below a petrol engine's.
        var band = this.Car.ShiftCeilingRpm - this.Car.ShiftFloorRpm;
        var upshift = Math.Clamp(
            this.Car.ShiftFloorRpm + (this.ThrottlePercent / 100 * band),
            this.Car.ShiftFloorRpm,
            this.Car.ShiftCeilingRpm
        );

        while (this.gear < ratios.Count - 1 && ratios[this.gear] * kph > upshift)
            this.gear++;

        while (this.gear > 0 && ratios[this.gear] * kph < this.Car.DownshiftRpm)
            this.gear--;
    }

    // ---- Turning the model into PIDs ---------------------------------------------------------------

    void Apply()
    {
        var car = this.Car;
        var kph = this.speed * 3.6;
        var moving = kph >= 4;
        var ratio = car.GearRatios[Math.Clamp(this.gear, 0, car.GearRatios.Count - 1)];
        var engineRpm = moving
            ? Math.Max(car.IdleRpm, ratio * kph)
            : car.IdleRpm + (car.IsElectric ? 0 : 25 * Math.Sin(this.clock * 1.7));

        // Deceleration with a gear engaged closes the injectors outright. It is the state that makes O2,
        // trim, load and fuel rate all move at once, so it is worth modelling properly. An EV has no
        // injectors to close - it regenerates instead, and none of the PIDs below exist on it anyway.
        var fuelCut = !car.IsElectric && this.acceleration < -0.5 && moving && engineRpm > 1300;

        // Throttle from the force being asked for as a fraction of what is available at this speed.
        // Power alone would under-read a hard launch badly - at 20 km/h a car is traction limited, not
        // power limited. Standing still the pedal is simply up, whatever the road load says.
        var tractive = (car.MassKg * this.acceleration) + this.RoadLoadNewtons();
        var capacity = Math.Min(car.PeakTractiveForceN, car.PeakPowerWatts / Math.Max(this.speed, 3.0));
        var idling = !moving && this.acceleration <= 0.05;
        var throttle = fuelCut || idling || tractive <= 0
            ? 0
            : Math.Clamp(5 + (95 * (tractive / capacity)), 0, 100);

        var load = fuelCut
            ? 4
            : moving
                ? Math.Clamp(10 + (throttle * 0.85), 0, 100)
                : 16 + (2 * Math.Sin(this.clock * 0.9));

        // Air mass from the engine's own displacement rather than a lookup: half a rev per intake stroke,
        // volumetric efficiency tracking load, air at about 1.18 g/L. A 6.7 L diesel therefore breathes
        // three times what a 2.0 L car does at the same revs, with no table to say so.
        var maf = car.IsElectric
            ? 0
            : fuelCut ? 0.4 : car.DisplacementLitres / 2 * (engineRpm / 60) * (load / 100 * 0.95) * 1.18;

        // Air mass over the stoichiometric ratio gives fuel mass; density turns that into litres. Petrol
        // is 14.7 and 745 g/L, diesel 14.5 and 832 - which is most of why the pickup drinks.
        var fuel = maf * 3600 / (car.AirFuelRatio * car.FuelDensityGramsPerLitre);

        this.Thermals(load, moving);
        this.Trip(fuel);

        this.Set(0x04, load);
        this.Set(0x05, this.coolantC);
        this.Set(0x06, fuelCut ? 0 : 2.4 * Math.Sin(this.clock * 1.1));           // short term trim, bank 1
        this.Set(0x0B, fuelCut ? 20 : Math.Clamp(20 + (load * 0.8), 20, 101));    // manifold pressure
        this.Set(0x0C, engineRpm);
        this.Set(0x0D, kph);
        this.Set(0x0E, Math.Clamp(8 + ((100 - load) * 0.28), 0, 45));             // timing advance
        this.Set(0x0F, this.intakeC);
        this.Set(0x10, maf);
        this.Set(0x11, Math.Clamp(13 + (throttle * 0.85), 0, 100));               // absolute throttle
        this.Set(0x1F, this.runtimeSeconds);
        this.Set(0x21, this.milKm);
        this.Set(0x2C, moving && !fuelCut && throttle < 40 ? 14 : 0);             // commanded EGR
        this.Set(0x2E, moving && this.coolantC > 70 ? 22 : 0);                    // evaporative purge
        this.Set(0x2F, this.fuelPercent);
        this.Set(0x31, this.clearedKm);
        this.Set(0x42, 14.2 - (load / 100 * 0.4));                                // control module voltage
        this.Set(0x43, load * 0.9);                                               // absolute load
        this.Set(0x45, throttle);                                                 // relative throttle
        this.Set(0x47, Math.Clamp(15 + (throttle * 0.8), 0, 100));                // throttle B
        this.Set(0x48, Math.Clamp(12 + (throttle * 0.8), 0, 100));                // throttle C
        this.Set(0x49, Math.Clamp(throttle * 0.9, 0, 100));                       // pedal D
        this.Set(0x4A, Math.Clamp(throttle * 0.85, 0, 100));                      // pedal E
        this.Set(0x4C, throttle);                                                 // commanded throttle actuator
        this.Set(0x4D, this.milMinutes);
        this.Set(0x4E, this.clearedMinutes);
        this.Set(0x5A, throttle);                                                 // relative pedal position
        this.Set(0x5C, this.oilC);
        this.Set(0x5E, fuel);
        this.Set(0x61, fuelCut ? -8 : Math.Clamp(throttle * 0.85, 0, 100));       // driver's demand torque
        this.Set(0x62, fuelCut ? -10 : Math.Clamp(load * 0.9, -125, 130));        // actual torque
        this.Set(0xA6, this.odometerKm);

        // Pre-cat sensor swings across stoich a couple of times a second in closed loop and pegs lean the
        // moment fuelling stops; the post-cat sensor barely moves, which is what a healthy catalyst looks like.
        this.Set(0x14, fuelCut ? 0.1 : 0.5 + (0.35 * Math.Sin(this.clock * 7.5)));
        this.Set(0x15, fuelCut ? 0.2 : 0.66 + (0.04 * Math.Sin(this.clock * 0.8)));

        this.ThrottlePercent = throttle;
        this.LoadPercent = load;
        this.SpeedKph = kph;
        this.Rpm = engineRpm;
        this.MassAirFlow = maf;
        this.FuelRate = fuel;
        this.CoolantTemperature = this.coolantC;
        this.GearLabel = car.GearRatios.Count == 1 ? "D" : this.gear < 0 ? "N" : (this.gear + 1).ToString();
    }

    void Thermals(double load, bool moving)
    {
        var ambient = this.Read(0x46, 21);

        // Warm-up is quick to 70 and slow after it, and the thermostat holds around 90 with the load
        // pushing it a few degrees higher.
        var coolantTarget = 88 + (load / 100 * 6);
        var tau = this.coolantC < 70 ? 90.0 : 220.0;
        this.coolantC += (coolantTarget - this.coolantC) * TickSeconds / tau;

        // Oil lags the coolant and runs a little hotter once it gets there.
        this.oilC += (this.coolantC + 6 - this.oilC) * TickSeconds / 320.0;

        // Intake air heat-soaks when stopped and cools off in the airflow once moving.
        var intakeTarget = ambient + (moving ? Math.Max(3, 12 - (this.speed * 0.35)) : 20);
        this.intakeC += (intakeTarget - this.intakeC) * TickSeconds / 45.0;
    }

    void Trip(double fuelLitresPerHour)
    {
        var km = this.speed * TickSeconds / 1000;

        this.odometerKm += km;
        this.clearedKm += km;
        this.runtimeSeconds += TickSeconds;
        this.clearedMinutes += TickSeconds / 60;

        // A tank of zero litres is an EV, which has no fuel level PID to fall.
        if (this.Car.TankLitres > 0)
            this.fuelPercent = Math.Max(0, this.fuelPercent - (fuelLitresPerHour * TickSeconds / 3600 / this.Car.TankLitres * 100));

        // Only counted while the lamp is on - which is exactly what a workshop reads these two for.
        if (state.MilOn)
        {
            this.milKm += km;
            this.milMinutes += TickSeconds / 60;
        }
    }

    void Publish(DrivingScenario scenario, DrivingStep step)
    {
        this.StepLabel = step.Label;

        var elapsed = TimeSpan.FromSeconds(scenario.Steps.Take(this.stepIndex).Sum(x => x.Seconds) + this.stepElapsed);
        this.ElapsedLabel = $"{Format(elapsed)} / {Format(scenario.Duration)}";
    }

    static string Format(TimeSpan value) => $"{(int)value.TotalMinutes}:{value.Seconds:00}";

    double Read(byte pid, double fallback) => state.Find(0x01, pid)?.Number ?? fallback;

    /// <summary>
    /// Writes a live value, unless the vehicle does not have that PID. Leaving an unsupported
    /// parameter alone keeps the vehicle honest: an EV showing a moving mass air flow reading it
    /// will never answer is worse than showing nothing.
    /// </summary>
    void Set(byte pid, double value)
    {
        if (state.Find(0x01, pid) is { IsSupported: true } parameter)
            parameter.Number = value;
    }
}
