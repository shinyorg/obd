namespace Sample.Maui.Emulator;

/// <summary>
/// The drives the emulator ships with. Each one loops, so picking one and walking away leaves a client
/// with hours of plausible, always-moving data.
/// </summary>
public static class DrivingScenarioCatalog
{
    public static DrivingScenario WarmIdle { get; } = new()
    {
        Name = "Warm idle",
        Summary = "Parked with the engine running. Coolant creeps up, everything else sits where an idling car sits - the baseline to compare a drive against.",
        Steps =
        [
            new("Idling in the driveway", DrivingAction.Idle, 0, 300)
        ]
    };

    public static DrivingScenario BusyHighway { get; } = new()
    {
        Name = "Busy highway",
        Summary = "Merge, lane changes, a truck cutting in, and a stop-and-go jam before it clears. About 13 minutes per lap.",
        Steps = [.. HighwaySteps()]
    };

    public static DrivingScenario CityDriving { get; } = new()
    {
        Name = "City driving",
        Summary = "Lights, a school zone, a roundabout and one emergency stop. Short bursts and long idles - about 8 minutes per lap.",
        Steps = [.. CitySteps()]
    };

    /// <summary>
    /// City out, highway across, city back. Roughly half an hour a lap, which is long enough to watch a
    /// client's reconnect and polling behaviour rather than just its decoding.
    /// </summary>
    public static DrivingScenario MixedCommute { get; } = new()
    {
        Name = "Mixed commute",
        Summary = "City streets, then the highway, then city streets again. About 30 minutes per lap - the one to leave running overnight.",
        Steps = [.. CitySteps(), .. HighwaySteps(), .. CitySteps()]
    };

    public static IReadOnlyList<DrivingScenario> All { get; } =
    [
        WarmIdle,
        CityDriving,
        BusyHighway,
        MixedCommute
    ];

    static IEnumerable<DrivingStep> HighwaySteps() =>
    [
        new("Waiting at the on-ramp", DrivingAction.Idle, 0, 12),
        new("Merging - hard up to speed", DrivingAction.Accelerate, 105, 22),
        new("Cruising in the middle lane", DrivingAction.Cruise, 105, 90),
        new("Traffic bunching up ahead", DrivingAction.Coast, 80, 16),
        new("Rolling along with the pack", DrivingAction.Cruise, 80, 60),
        new("Pulling out to overtake", DrivingAction.Accelerate, 125, 18),
        new("Cruising in the fast lane", DrivingAction.Cruise, 125, 75),
        new("Truck cuts in - emergency stop", DrivingAction.HarshBrake, 70, 6),
        new("Sitting behind the truck", DrivingAction.Cruise, 72, 45),
        new("Clear again - back up to speed", DrivingAction.Accelerate, 110, 16),
        new("Cruising", DrivingAction.Cruise, 110, 120),
        new("Brake lights all the way down", DrivingAction.Brake, 15, 12),
        new("Crawling in the jam", DrivingAction.Cruise, 12, 40),
        new("Jam stops dead", DrivingAction.Brake, 0, 6),
        new("Stopped in the queue", DrivingAction.Idle, 0, 20),
        new("Queue moves", DrivingAction.Accelerate, 45, 12),
        new("Queue stops again", DrivingAction.HarshBrake, 0, 5),
        new("Stopped in the queue", DrivingAction.Idle, 0, 15),
        new("Jam clears - back up to speed", DrivingAction.Accelerate, 100, 25),
        new("Cruising home", DrivingAction.Cruise, 100, 120),
        new("Onto the exit ramp", DrivingAction.Brake, 55, 12),
        new("Coasting down the ramp", DrivingAction.Coast, 40, 10),
        new("Stopping at the ramp lights", DrivingAction.Brake, 0, 10),
        new("Waiting at the ramp lights", DrivingAction.Idle, 0, 12)
    ];

    static IEnumerable<DrivingStep> CitySteps() =>
    [
        new("Waiting at the lights", DrivingAction.Idle, 0, 20),
        new("Pulling away from the lights", DrivingAction.Accelerate, 50, 12),
        new("Along the main road", DrivingAction.Cruise, 50, 35),
        new("Slowing for a red light", DrivingAction.Brake, 0, 8),
        new("Stopped at the lights", DrivingAction.Idle, 0, 25),
        new("Away again", DrivingAction.Accelerate, 40, 9),
        new("Following traffic", DrivingAction.Cruise, 40, 20),
        new("Pedestrian steps out - emergency stop", DrivingAction.HarshBrake, 0, 4),
        new("Stopped, letting them cross", DrivingAction.Idle, 0, 12),
        new("Into the school zone", DrivingAction.Accelerate, 30, 8),
        new("Crawling through the school zone", DrivingAction.Cruise, 30, 40),
        new("Lifting off for the roundabout", DrivingAction.Coast, 20, 8),
        new("Through the roundabout", DrivingAction.Cruise, 22, 12),
        new("Out of the roundabout", DrivingAction.Accelerate, 55, 14),
        new("Along the arterial", DrivingAction.Cruise, 55, 45),
        new("Bus pulls out", DrivingAction.Brake, 25, 6),
        new("Stuck behind the bus", DrivingAction.Cruise, 25, 25),
        new("Past the bus", DrivingAction.Accelerate, 50, 10),
        new("Back up to the limit", DrivingAction.Cruise, 50, 30),
        new("Slowing for the intersection", DrivingAction.Brake, 0, 9),
        new("Long light", DrivingAction.Idle, 0, 30),
        new("Away on the green", DrivingAction.Accelerate, 45, 10),
        new("Last few blocks", DrivingAction.Cruise, 45, 25),
        new("Rolling up to the driveway", DrivingAction.Coast, 0, 12),
        new("Parked, engine running", DrivingAction.Idle, 0, 20)
    ];
}
