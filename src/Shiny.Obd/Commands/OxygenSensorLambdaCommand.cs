namespace Shiny.Obd.Commands;

/// <summary>
/// Wideband Oxygen Sensor (Mode 01, PIDs 0x24-0x2B or 0x34-0x3B) - Returns the sensor's air-fuel
/// equivalence ratio (lambda) alongside either its voltage or its pump current
/// </summary>
/// <remarks>
/// Lambda is the reading that matters: 1.0 is stoichiometric, below 1 is rich, above 1 is lean, and
/// unlike a narrowband voltage it stays meaningful across the whole range rather than only near the
/// switching point. Multiply by 14.7 for a petrol air-fuel ratio.
///
/// <para>
/// The same eight physical sensors answer two PID blocks that differ only in the second value:
/// 0x24-0x2B reports voltage, 0x34-0x3B reports pump current. Vehicles commonly support one, and
/// current is the more direct signal on a modern sensor — zero current is lambda 1, positive is lean.
/// Probe with <see cref="SupportedPidsCommand"/> rather than assuming.
/// </para>
///
/// <para>
/// A narrowband sensor answers <see cref="OxygenSensorVoltageCommand"/> (PIDs 0x14-0x1B) instead. Do
/// not compare the two voltages — they are different measurements that happen to share a unit.
/// </para>
/// </remarks>
public class OxygenSensorLambdaCommand : ObdCommand<OxygenSensorLambda>
{
    readonly bool reportsCurrent;

    OxygenSensorLambdaCommand(int sensorIndex, byte pid, bool reportsCurrent) : base(0x01, pid)
    {
        this.SensorIndex = sensorIndex;
        this.reportsCurrent = reportsCurrent;
    }

    /// <summary>
    /// The sensor addressed, 1-8. Use <see cref="OxygenSensorLayout.Position"/> to turn this into a
    /// bank and position.
    /// </summary>
    public int SensorIndex { get; }

    /// <summary>Lambda and sensor voltage for sensor <paramref name="sensorIndex"/> (1-8), PID <c>0x23 + index</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside 1-8.</exception>
    public static OxygenSensorLambdaCommand WithVoltage(int sensorIndex)
        => new(Validate(sensorIndex), (byte)(0x23 + sensorIndex), reportsCurrent: false);

    /// <summary>Lambda and pump current for sensor <paramref name="sensorIndex"/> (1-8), PID <c>0x33 + index</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside 1-8.</exception>
    public static OxygenSensorLambdaCommand WithCurrent(int sensorIndex)
        => new(Validate(sensorIndex), (byte)(0x33 + sensorIndex), reportsCurrent: true);

    protected override OxygenSensorLambda ParseData(byte[] data)
    {
        if (data.Length < 4)
            throw new ObdException("Wideband oxygen sensor response requires 4 data bytes");

        var lambda = 2.0 / 65536.0 * ((data[0] << 8) | data[1]);
        var second = (data[2] << 8) | data[3];

        return this.reportsCurrent
            ? new OxygenSensorLambda(lambda, null, (second / 256.0) - 128.0)
            : new OxygenSensorLambda(lambda, 8.0 / 65536.0 * second, null);
    }

    static int Validate(int sensorIndex)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sensorIndex, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sensorIndex, 8);
        return sensorIndex;
    }
}

/// <summary>The result of <see cref="OxygenSensorLambdaCommand"/>.</summary>
/// <param name="Lambda">
/// Air-fuel equivalence ratio, 0 to just under 2. 1.0 is stoichiometric; multiply by 14.7 for a
/// petrol air-fuel ratio.
/// </param>
/// <param name="Volts">Sensor voltage (0 to just under 8 V), or null when this PID reports current instead.</param>
/// <param name="Milliamps">Pump current (-128 to just under 128 mA), or null when this PID reports voltage instead.</param>
public readonly record struct OxygenSensorLambda(double Lambda, double? Volts, double? Milliamps)
{
    /// <summary>
    /// Lambda expressed as a petrol air-fuel ratio (lambda x 14.7).
    /// </summary>
    /// <remarks>
    /// 14.7:1 is the stoichiometric ratio for petrol only. For E85 the figure is about 9.8 and for
    /// diesel about 14.5, so use <see cref="Lambda"/> directly when the fuel is not known — that is
    /// what <see cref="FuelTypeCommand"/> is for.
    /// </remarks>
    public double PetrolAirFuelRatio => this.Lambda * 14.7;
}

/// <summary>
/// Commanded Air-Fuel Equivalence Ratio (Mode 01, PID 0x44) - Returns the lambda the ECU is aiming for
/// </summary>
/// <remarks>
/// The target, where <see cref="OxygenSensorLambdaCommand"/> is the measurement. In closed loop this
/// sits at 1.0 and the interesting number is the gap to what the sensor actually reports; under load
/// the ECU commands rich deliberately, which is why a lean-of-target reading during acceleration means
/// something quite different from the same reading at idle.
/// </remarks>
public class CommandedAirFuelRatioCommand() : ObdCommand<double>(0x01, 0x44)
{
    protected override double ParseData(byte[] data)
    {
        if (data.Length < 2)
            throw new ObdException("Commanded air-fuel ratio response requires 2 data bytes");

        return 2.0 / 65536.0 * ((data[0] << 8) | data[1]);
    }
}
