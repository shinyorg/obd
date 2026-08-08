namespace Shiny.Obd.Commands;

/// <summary>
/// Oxygen Sensor Voltage (Mode 01, PIDs 0x14-0x1B) - Returns a narrowband sensor's output voltage and
/// the short term fuel trim the ECU is applying because of it
/// </summary>
/// <remarks>
/// This is the reading that explains a fuel trim. <see cref="FuelTrimCommand"/> reports the ECU's
/// correction; this reports the measurement driving it, and the pair is what separates a genuine
/// mixture problem from a failing sensor.
///
/// <para>
/// A healthy **upstream** narrowband sensor oscillates roughly 0.1-0.9 V several times a second once
/// hot — a reading parked mid-range is the signature of a lazy or cold sensor, not of a perfect
/// mixture. A **downstream** sensor should sit fairly steady around 0.6-0.7 V; when it starts mirroring
/// the upstream sensor's swing, the catalyst has stopped storing oxygen.
/// </para>
///
/// <para>
/// One reading is worth very little here. Sample over several seconds and look at the shape.
/// </para>
///
/// <para>
/// Wideband sensors answer <see cref="OxygenSensorLambdaCommand"/> (PIDs 0x24-0x2B or 0x34-0x3B)
/// instead and report a voltage that is not comparable to this one. Probe with
/// <see cref="SupportedPidsCommand"/> to find out which family a vehicle has.
/// </para>
/// </remarks>
public class OxygenSensorVoltageCommand : ObdCommand<OxygenSensorVoltage>
{
    OxygenSensorVoltageCommand(int sensorIndex, byte pid) : base(0x01, pid)
        => this.SensorIndex = sensorIndex;

    /// <summary>
    /// The sensor addressed, 1-8. Use <see cref="OxygenSensorLayout.Position"/> to turn this into a
    /// bank and position — what it maps to depends on which layout PID the vehicle answers.
    /// </summary>
    public int SensorIndex { get; }

    /// <summary>Reads sensor <paramref name="sensorIndex"/> (1-8), PID <c>0x13 + index</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside 1-8.</exception>
    public static OxygenSensorVoltageCommand Sensor(int sensorIndex)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sensorIndex, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sensorIndex, 8);

        return new OxygenSensorVoltageCommand(sensorIndex, (byte)(0x13 + sensorIndex));
    }

    protected override OxygenSensorVoltage ParseData(byte[] data)
    {
        if (data.Length < 2)
            throw new ObdException("Oxygen sensor voltage response requires 2 data bytes");

        // 0xFF in byte B is the standard "this sensor is not used in trim calculation" marker. It
        // scales to +99.2%, which is a plausible-looking number and exactly the sort of thing that
        // ends up on a graph as a real reading if it is not special-cased.
        double? trim = data[1] == 0xFF ? null : (data[1] * 100.0 / 128.0) - 100.0;

        return new OxygenSensorVoltage(data[0] / 200.0, trim);
    }
}

/// <summary>The result of <see cref="OxygenSensorVoltageCommand"/>.</summary>
/// <param name="Volts">Sensor output, 0-1.275 V.</param>
/// <param name="ShortTermFuelTrim">
/// The short term trim associated with this sensor as a percentage, or null when the vehicle marked
/// the sensor as not used in the trim calculation.
/// </param>
public readonly record struct OxygenSensorVoltage(double Volts, double? ShortTermFuelTrim);
