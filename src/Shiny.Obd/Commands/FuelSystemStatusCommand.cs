namespace Shiny.Obd.Commands;

/// <summary>
/// Fuel System Status (Mode 01, PID 0x03) - Returns the loop state of one or two fuel systems
/// </summary>
/// <remarks>
/// Read this alongside <see cref="FuelTrimCommand"/>. Trims are only meaningful in closed loop —
/// in open loop the ECU is running a fixed map with no oxygen sensor feedback, so a trim figure
/// there says nothing about a leak or a lazy sensor and must not be trended as though it did.
/// </remarks>
public class FuelSystemStatusCommand() : ObdCommand<FuelSystemStatus>(0x01, 0x03)
{
    protected override FuelSystemStatus ParseData(byte[] data)
    {
        if (data.Length < 1)
            throw new ObdException("Fuel system status response requires at least 1 data byte");

        // Byte B is absent on plenty of vehicles and zero on most of the rest — a vehicle with one
        // fuel system reports zero there, which is *not* the same as system 1's zero (engine off).
        var second = data.Length > 1 && data[1] != 0 ? Describe(data[1]) : null;
        return new FuelSystemStatus(Describe(data[0]), second);
    }

    static FuelSystemState? Describe(byte value) => value switch
    {
        0x00 => FuelSystemState.Off,
        0x01 => FuelSystemState.OpenLoopEngineCold,
        0x02 => FuelSystemState.ClosedLoop,
        0x04 => FuelSystemState.OpenLoopLoadOrDeceleration,
        0x08 => FuelSystemState.OpenLoopSystemFailure,
        0x10 => FuelSystemState.ClosedLoopWithFault,
        _ => null
    };
}

/// <summary>The result of <see cref="FuelSystemStatusCommand"/>.</summary>
/// <param name="System1">The primary fuel system, or null when the vehicle reported a value outside the standard set.</param>
/// <param name="System2">The second fuel system, or null when the vehicle has only one.</param>
public readonly record struct FuelSystemStatus(FuelSystemState? System1, FuelSystemState? System2)
{
    /// <summary>
    /// Whether the primary system is running closed loop, and so whether a fuel trim reading taken
    /// at the same moment is worth anything.
    /// </summary>
    public bool IsClosedLoop
        => this.System1 is FuelSystemState.ClosedLoop or FuelSystemState.ClosedLoopWithFault;
}

/// <summary>The loop state of one fuel system, as reported by mode 01 PID 0x03.</summary>
public enum FuelSystemState
{
    /// <summary>The engine is not running.</summary>
    Off,

    /// <summary>Open loop — the engine has not reached operating temperature yet.</summary>
    OpenLoopEngineCold,

    /// <summary>Closed loop — using oxygen sensor feedback to trim the mixture.</summary>
    ClosedLoop,

    /// <summary>Open loop — engine load, or fuel cut on a closed throttle.</summary>
    OpenLoopLoadOrDeceleration,

    /// <summary>Open loop — driven there by a system failure.</summary>
    OpenLoopSystemFailure,

    /// <summary>Closed loop, but with a fault in the feedback system.</summary>
    ClosedLoopWithFault
}
