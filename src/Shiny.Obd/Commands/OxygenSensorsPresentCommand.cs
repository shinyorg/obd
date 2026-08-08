namespace Shiny.Obd.Commands;

/// <summary>
/// Oxygen Sensors Present (Mode 01, PID 0x13 or 0x1D) - Returns which O2 sensors the vehicle has,
/// and which physical position each of the sensor PIDs refers to
/// </summary>
/// <remarks>
/// Read this **before** any per-sensor reading, and not merely to avoid querying a sensor that is not
/// there. A vehicle answers either PID 0x13 or PID 0x1D, never both, and which one it answers changes
/// what the sensor PIDs mean:
///
/// <list type="bullet">
/// <item>PID 0x13 — two banks of up to four sensors. PID 0x16 is bank 1, sensor 3.</item>
/// <item>PID 0x1D — four banks of two sensors. PID 0x16 is bank 2, sensor 1.</item>
/// </list>
///
/// <para>
/// The sensor PIDs (0x14-0x1B, 0x24-0x2B, 0x34-0x3B) are identical in both cases — only the meaning
/// moves. Label a reading from the wrong layout and you send someone to replace the downstream sensor
/// on the wrong bank, so <see cref="OxygenSensorLayout.Position"/> exists to do that mapping rather
/// than leaving it to the caller.
/// </para>
/// </remarks>
public class OxygenSensorsPresentCommand(byte pid) : ObdCommand<OxygenSensorLayout>(0x01, pid)
{
    /// <summary>Two banks of up to four sensors each (PID 0x13).</summary>
    public static OxygenSensorsPresentCommand TwoBanks() => new(0x13);

    /// <summary>Four banks of up to two sensors each (PID 0x1D).</summary>
    public static OxygenSensorsPresentCommand FourBanks() => new(0x1D);

    protected override OxygenSensorLayout ParseData(byte[] data)
    {
        if (data.Length < 1)
            throw new ObdException("Oxygen sensors present response requires 1 data byte");

        var kind = this.Pid == 0x1D
            ? OxygenSensorBankLayout.FourBanksOfTwo
            : OxygenSensorBankLayout.TwoBanksOfFour;

        var present = new List<OxygenSensorPosition>(8);
        for (var i = 0; i < 8; i++)
        {
            // Bit 0 is sensor 1, ascending — the opposite order to the supported-PID bitmask, which
            // is MSB-first. Getting these two the same way round is a classic way to report a
            // vehicle's sensors mirrored.
            if ((data[0] & (1 << i)) != 0)
                present.Add(OxygenSensorLayout.PositionOf(i + 1, kind));
        }

        return new OxygenSensorLayout(kind, present);
    }
}

/// <summary>The result of <see cref="OxygenSensorsPresentCommand"/>.</summary>
/// <param name="Layout">Which bank/sensor arrangement this vehicle reports.</param>
/// <param name="Sensors">The sensors actually fitted, in sensor-index order.</param>
public readonly record struct OxygenSensorLayout(
    OxygenSensorBankLayout Layout,
    IReadOnlyList<OxygenSensorPosition> Sensors
)
{
    /// <summary>
    /// Where sensor <paramref name="sensorIndex"/> (1-8) physically sits, under this vehicle's layout.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside 1-8.</exception>
    public OxygenSensorPosition Position(int sensorIndex) => PositionOf(sensorIndex, this.Layout);

    /// <summary>Whether the vehicle reported sensor <paramref name="sensorIndex"/> (1-8) as fitted.</summary>
    public bool IsPresent(int sensorIndex)
    {
        foreach (var sensor in this.Sensors)
        {
            if (sensor.SensorIndex == sensorIndex)
                return true;
        }
        return false;
    }

    internal static OxygenSensorPosition PositionOf(int sensorIndex, OxygenSensorBankLayout layout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sensorIndex, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sensorIndex, 8);

        var perBank = layout == OxygenSensorBankLayout.FourBanksOfTwo ? 2 : 4;
        var zero = sensorIndex - 1;

        return new OxygenSensorPosition(
            Bank: (zero / perBank) + 1,
            Sensor: (zero % perBank) + 1,
            SensorIndex: sensorIndex
        );
    }
}

/// <summary>Where one oxygen sensor sits.</summary>
/// <param name="Bank">The cylinder bank, from 1.</param>
/// <param name="Sensor">The position in the exhaust for that bank, from 1 (upstream of the catalyst).</param>
/// <param name="SensorIndex">
/// The 1-8 index used to address this sensor's PIDs — <c>0x13 + index</c> for voltage,
/// <c>0x23 + index</c> for lambda/voltage, <c>0x33 + index</c> for lambda/current.
/// </param>
public readonly record struct OxygenSensorPosition(int Bank, int Sensor, int SensorIndex)
{
    /// <summary>Renders in the conventional <c>B1S2</c> shorthand a repair manual uses.</summary>
    public override string ToString() => $"B{this.Bank}S{this.Sensor}";
}

/// <summary>Which bank/sensor arrangement a vehicle reports its oxygen sensors in.</summary>
public enum OxygenSensorBankLayout
{
    /// <summary>Two banks of up to four sensors (mode 01 PID 0x13).</summary>
    TwoBanksOfFour,

    /// <summary>Four banks of up to two sensors (mode 01 PID 0x1D).</summary>
    FourBanksOfTwo
}
