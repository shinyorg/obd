namespace Shiny.Obd.Commands;

/// <summary>
/// Driver's Demand Engine Torque (Mode 01, PID 0x61) - Returns the torque the driver is asking for, as
/// a percentage of reference torque
/// </summary>
/// <remarks>
/// What the pedal is asking the engine to do. The gap between this and
/// <see cref="ActualEngineTorqueCommand"/> is the engine failing to deliver what was requested — a
/// limp-mode or boost-leak signature that no single reading shows on its own.
/// </remarks>
public class DriverDemandTorqueCommand() : ObdCommand<int>(0x01, 0x61)
{
    protected override int ParseData(byte[] data)
    {
        if (data.Length < 1)
            throw new ObdException("Driver's demand torque response requires 1 data byte");

        return data[0] - 125;
    }
}

/// <summary>
/// Actual Engine Torque (Mode 01, PID 0x62) - Returns the torque the engine is producing, as a
/// percentage of reference torque
/// </summary>
/// <remarks>
/// A percentage, not a figure in newton-metres — pair it with <see cref="ReferenceTorqueCommand"/> and
/// <see cref="EnginePower"/> to get real units. Negative values are normal and mean the engine is
/// being driven rather than driving (overrun, engine braking).
/// </remarks>
public class ActualEngineTorqueCommand() : ObdCommand<int>(0x01, 0x62)
{
    protected override int ParseData(byte[] data)
    {
        if (data.Length < 1)
            throw new ObdException("Actual engine torque response requires 1 data byte");

        return data[0] - 125;
    }
}

/// <summary>
/// Engine Reference Torque (Mode 01, PID 0x63) - Returns the engine's 100% torque figure in newton-metres
/// </summary>
/// <remarks>
/// The denominator that turns every other torque PID from a percentage into a measurement. It is a
/// constant for the engine, so read it once per connection rather than per sample — see
/// <see cref="EnginePower"/>.
/// </remarks>
public class ReferenceTorqueCommand() : ObdCommand<int>(0x01, 0x63)
{
    protected override int ParseData(byte[] data)
    {
        if (data.Length < 2)
            throw new ObdException("Engine reference torque response requires 2 data bytes");

        return (data[0] << 8) | data[1];
    }
}

/// <summary>
/// Engine Percent Torque Data (Mode 01, PID 0x64) - Returns the engine's torque at idle and at four
/// calibration points, each as a percentage of reference torque
/// </summary>
/// <remarks>
/// The engine's torque map as the ECU holds it. Chiefly useful as a fingerprint: the points are fixed
/// by the calibration, so a set that differs from what the same vehicle reported previously means the
/// ECU has been reflashed — the same signal <see cref="CalibrationIdCommand"/> gives, from a different
/// direction.
/// </remarks>
public class EnginePercentTorqueDataCommand() : ObdCommand<EnginePercentTorqueData>(0x01, 0x64)
{
    protected override EnginePercentTorqueData ParseData(byte[] data)
    {
        if (data.Length < 5)
            throw new ObdException("Engine percent torque data response requires 5 data bytes");

        return new EnginePercentTorqueData(
            data[0] - 125,
            data[1] - 125,
            data[2] - 125,
            data[3] - 125,
            data[4] - 125
        );
    }
}

/// <summary>The result of <see cref="EnginePercentTorqueDataCommand"/>, all as percentages of reference torque.</summary>
/// <param name="Idle">Torque at idle.</param>
/// <param name="Point1">Engine point 1.</param>
/// <param name="Point2">Engine point 2.</param>
/// <param name="Point3">Engine point 3.</param>
/// <param name="Point4">Engine point 4.</param>
public readonly record struct EnginePercentTorqueData(int Idle, int Point1, int Point2, int Point3, int Point4);

/// <summary>
/// Turns the torque PIDs into real units.
/// </summary>
/// <remarks>
/// Mode 01 reports torque as a percentage of a reference figure, so neither PID means anything alone.
/// <see cref="ReferenceTorqueCommand"/> is a constant for the engine — read it once and reuse it,
/// rather than paying for it on every sample of a live gauge.
///
/// <para>
/// Every vehicle that reports these PIDs is reporting the **engine's** output, at the flywheel and
/// before the drivetrain. It is not a substitute for a chassis dyno and will read higher than one.
/// </para>
/// </remarks>
public static class EnginePower
{
    /// <summary>
    /// Actual torque in newton-metres, from the percentage and the engine's reference figure.
    /// </summary>
    /// <param name="actualTorquePercent">From <see cref="ActualEngineTorqueCommand"/>.</param>
    /// <param name="referenceTorqueNm">From <see cref="ReferenceTorqueCommand"/>.</param>
    public static double TorqueNm(int actualTorquePercent, int referenceTorqueNm)
        => actualTorquePercent / 100.0 * referenceTorqueNm;

    /// <summary>
    /// Power in kilowatts, from torque and engine speed.
    /// </summary>
    /// <param name="torqueNm">Torque in newton-metres — see <see cref="TorqueNm"/>.</param>
    /// <param name="rpm">From <see cref="EngineRpmCommand"/>.</param>
    public static double KilowattsFromTorque(double torqueNm, int rpm)
        => torqueNm * rpm * 2 * Math.PI / 60_000.0;

    /// <summary>
    /// Power in kilowatts, straight from the three readings.
    /// </summary>
    public static double Kilowatts(int actualTorquePercent, int referenceTorqueNm, int rpm)
        => KilowattsFromTorque(TorqueNm(actualTorquePercent, referenceTorqueNm), rpm);

    /// <summary>
    /// Power in metric horsepower (PS), straight from the three readings.
    /// </summary>
    /// <remarks>
    /// Metric horsepower, not mechanical: 1 PS is 735.5 W, where the imperial hp is 745.7 W. The two
    /// differ by about 1.4%, which is small enough to look like measurement noise and large enough to
    /// make two apps disagree about the same car.
    /// </remarks>
    public static double MetricHorsepower(int actualTorquePercent, int referenceTorqueNm, int rpm)
        => Kilowatts(actualTorquePercent, referenceTorqueNm, rpm) * 1000.0 / 735.49875;

    /// <summary>
    /// Power in mechanical horsepower (hp), straight from the three readings.
    /// </summary>
    public static double MechanicalHorsepower(int actualTorquePercent, int referenceTorqueNm, int rpm)
        => Kilowatts(actualTorquePercent, referenceTorqueNm, rpm) * 1000.0 / 745.69987;
}
