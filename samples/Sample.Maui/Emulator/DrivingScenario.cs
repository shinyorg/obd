namespace Sample.Maui.Emulator;

/// <summary>
/// What the driver is doing during a step. The action decides how hard the speed moves toward
/// <see cref="DrivingStep.TargetSpeedKph"/> - which is what separates lifting off in traffic from
/// standing on the brake pedal.
/// </summary>
public enum DrivingAction
{
    /// <summary>Stopped with the engine running. Any remaining speed is scrubbed off first.</summary>
    Idle,

    /// <summary>Under power up to the target speed.</summary>
    Accelerate,

    /// <summary>Holding the target speed, with the small ripple a real driver never quite avoids.</summary>
    Cruise,

    /// <summary>Off the throttle, slowing on drag alone until the target is reached.</summary>
    Coast,

    /// <summary>Normal braking - roughly 0.3 g.</summary>
    Brake,

    /// <summary>Emergency braking - roughly 0.7 g, which is where fuel cut and pedal PIDs get interesting.</summary>
    HarshBrake
}

/// <summary>One leg of a drive: what the car is doing, where it is heading, and for how long.</summary>
/// <param name="Label">Shown while the step is playing, so the numbers on screen have a story attached.</param>
/// <param name="Action">How the speed is driven toward <paramref name="TargetSpeedKph"/>.</param>
/// <param name="TargetSpeedKph">
/// The speed the step is aiming at. For <see cref="DrivingAction.Brake"/>, <see cref="DrivingAction.HarshBrake"/>
/// and <see cref="DrivingAction.Coast"/> this is a floor - the car slows to it and then holds.
/// </param>
/// <param name="Seconds">How long the step runs, regardless of whether the target was reached.</param>
public sealed record DrivingStep(string Label, DrivingAction Action, double TargetSpeedKph, double Seconds);

/// <summary>
/// A named drive the emulator can play back: a list of steps and whether it repeats when it runs out.
/// </summary>
/// <remarks>
/// Scenarios exist so a client can be left running against the emulator for an hour and see values
/// that move the way a vehicle's do - rather than the flat line a manually edited parameter gives you.
/// </remarks>
public sealed class DrivingScenario
{
    public required string Name { get; init; }

    /// <summary>One line describing the drive, shown under the picker.</summary>
    public required string Summary { get; init; }

    public required IReadOnlyList<DrivingStep> Steps { get; init; }

    /// <summary>Restart from the first step when the last one ends. On by default - long tests are the point.</summary>
    public bool Loops { get; init; } = true;

    public TimeSpan Duration => TimeSpan.FromSeconds(this.Steps.Sum(x => x.Seconds));

    // The Picker binds straight to the scenario objects, so this is what shows in the dropdown.
    public override string ToString() => this.Name;
}
