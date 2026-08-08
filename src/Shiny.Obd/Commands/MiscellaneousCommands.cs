namespace Shiny.Obd.Commands;

/// <summary>
/// Ethanol Fuel Percentage (Mode 01, PID 0x52) - Returns the ethanol content of the fuel in the tank
/// </summary>
/// <remarks>
/// Flex-fuel vehicles only. Worth reading before interpreting anything mixture-related: E85 is
/// stoichiometric at about 9.8:1 rather than petrol's 14.7:1, so a fuel trim or lambda figure means
/// something different on a tank of E85, and <see cref="OxygenSensorLambda.PetrolAirFuelRatio"/> is
/// simply wrong there.
/// </remarks>
public class EthanolFuelPercentCommand() : ObdCommand<double>(0x01, 0x52)
{
    protected override double ParseData(byte[] data)
    {
        if (data.Length < 1)
            throw new ObdException("Ethanol fuel percent response requires 1 data byte");

        return data[0] * 100.0 / 255.0;
    }
}

/// <summary>
/// Absolute Load Value (Mode 01, PID 0x43) - Returns cylinder air mass as a percentage of the maximum
/// at sea level
/// </summary>
/// <remarks>
/// Unlike <see cref="CalculatedEngineLoadCommand"/>, this is not capped at 100%: a naturally aspirated
/// engine peaks around 95%, and a boosted one goes well above it, up to 400% or so. That makes it the
/// better load axis for comparing readings across vehicles, and the one to use when logging against
/// boost.
/// </remarks>
public class AbsoluteLoadValueCommand() : ObdCommand<double>(0x01, 0x43)
{
    protected override double ParseData(byte[] data)
    {
        if (data.Length < 2)
            throw new ObdException("Absolute load value response requires 2 data bytes");

        return ((data[0] << 8) | data[1]) * 100.0 / 255.0;
    }
}

/// <summary>
/// Warm-ups Since Codes Cleared (Mode 01, PID 0x30) - Returns how many warm-up cycles have completed
/// since the last reset
/// </summary>
/// <remarks>
/// The other half of the readiness question. <see cref="MonitorStatusCommand"/> says a monitor has not
/// run; this says whether the vehicle has had the chance. A car showing not-ready with two warm-ups on
/// the clock has simply not been driven enough since its codes were cleared, which is a completely
/// different conversation from one showing not-ready after forty.
/// </remarks>
public class WarmUpsSinceCodesClearedCommand() : ObdCommand<int>(0x01, 0x30)
{
    protected override int ParseData(byte[] data)
    {
        if (data.Length < 1)
            throw new ObdException("Warm-ups since codes cleared response requires 1 data byte");

        return data[0];
    }
}

/// <summary>
/// Relative Throttle Position (Mode 01, PID 0x45) - Returns throttle opening relative to its learned
/// closed position
/// </summary>
/// <remarks>
/// This is the one to display. <see cref="ThrottlePositionCommand"/> (PID 0x11) is *absolute* and
/// carries a 12-18% closed floor that varies by vehicle, so a UI built on it shows a throttle that is
/// never shut; this one is already referenced to the learned stop and reads 0% at rest.
/// </remarks>
public class RelativeThrottlePositionCommand() : ObdCommand<double>(0x01, 0x45)
{
    protected override double ParseData(byte[] data)
    {
        if (data.Length < 1)
            throw new ObdException("Relative throttle position response requires 1 data byte");

        return data[0] * 100.0 / 255.0;
    }
}

/// <summary>
/// Absolute Throttle Position B/C (Mode 01, PIDs 0x47/0x48) - Returns a secondary throttle sensor's
/// position
/// </summary>
/// <remarks>
/// Drive-by-wire throttle bodies carry redundant position sensors, and the ECU cross-checks them —
/// a disagreement between B and C is what sets the throttle-position-correlation codes and puts a car
/// into limp mode. Reading both is how you confirm that diagnosis.
/// </remarks>
public class AbsoluteThrottlePositionCommand(byte pid) : ObdCommand<double>(0x01, pid)
{
    /// <summary>Throttle position sensor B (PID 0x47).</summary>
    public static AbsoluteThrottlePositionCommand B() => new(0x47);

    /// <summary>Throttle position sensor C (PID 0x48).</summary>
    public static AbsoluteThrottlePositionCommand C() => new(0x48);

    protected override double ParseData(byte[] data)
    {
        if (data.Length < 1)
            throw new ObdException("Absolute throttle position response requires 1 data byte");

        return data[0] * 100.0 / 255.0;
    }
}

/// <summary>
/// Fuel Injection Timing (Mode 01, PID 0x5D) - Returns injection timing in degrees relative to top
/// dead centre
/// </summary>
/// <remarks>
/// Negative is before top dead centre. Chiefly a diesel reading, where injection timing is the primary
/// lever on combustion noise and NOx, and where a value drifting from the commanded schedule points at
/// the injectors or the high-pressure pump.
/// </remarks>
public class FuelInjectionTimingCommand() : ObdCommand<double>(0x01, 0x5D)
{
    protected override double ParseData(byte[] data)
    {
        if (data.Length < 2)
            throw new ObdException("Fuel injection timing response requires 2 data bytes");

        return (((data[0] << 8) | data[1]) / 128.0) - 210.0;
    }
}

/// <summary>
/// Engine Run Time (Mode 01, PID 0x7F) - Returns total engine run time, total idle time and total time
/// with power take-off engaged
/// </summary>
/// <remarks>
/// Lifetime counters, not trip counters — these do not reset when codes are cleared, which makes them
/// the closest thing OBD-II has to an hour meter. Idle time as a fraction of total is the number a
/// fleet cares about, and total run time is what service intervals on commercial vehicles are actually
/// keyed to.
/// </remarks>
public class EngineRunTimeCommand() : ObdCommand<EngineRunTime>(0x01, 0x7F)
{
    protected override EngineRunTime ParseData(byte[] data)
    {
        // A leading byte says which of the three counters are supported, then three 4-byte counters.
        if (data.Length < 13)
            throw new ObdException("Engine run time response requires 13 data bytes");

        return new EngineRunTime(
            Seconds(data, 1),
            Seconds(data, 5),
            Seconds(data, 9)
        );
    }

    static TimeSpan Seconds(byte[] data, int offset)
    {
        var seconds = ((uint)data[offset] << 24)
            | ((uint)data[offset + 1] << 16)
            | ((uint)data[offset + 2] << 8)
            | data[offset + 3];

        return TimeSpan.FromSeconds(seconds);
    }
}

/// <summary>The result of <see cref="EngineRunTimeCommand"/>.</summary>
/// <param name="Total">Total engine run time.</param>
/// <param name="Idle">Total time spent idling.</param>
/// <param name="PowerTakeOff">Total time with power take-off engaged. Zero on anything without a PTO.</param>
public readonly record struct EngineRunTime(TimeSpan Total, TimeSpan Idle, TimeSpan PowerTakeOff)
{
    /// <summary>
    /// Idle time as a percentage of total run time, or null when the engine has never run.
    /// </summary>
    public double? IdleFraction => this.Total > TimeSpan.Zero
        ? this.Idle.TotalSeconds / this.Total.TotalSeconds * 100.0
        : null;
}
