namespace Shiny.Obd.Commands;

/// <summary>
/// Fuel Pressure (Mode 01, PID 0x0A) - Returns gauge fuel pressure in kPa
/// </summary>
/// <remarks>
/// The low-pressure side, and a coarse reading — 3 kPa per bit, up to 765 kPa. Found on port-injection
/// vehicles; a direct-injection engine reports rail pressure instead, on
/// <see cref="FuelRailGaugePressureCommand"/>, at figures two orders of magnitude higher.
/// </remarks>
public class FuelPressureCommand() : ObdCommand<int>(0x01, 0x0A)
{
    protected override int ParseData(byte[] data)
    {
        if (data.Length < 1)
            throw new ObdException("Fuel pressure response requires 1 data byte");

        return data[0] * 3;
    }
}

/// <summary>
/// Fuel Rail Pressure (Mode 01, PID 0x22) - Returns rail pressure in kPa, relative to manifold vacuum
/// </summary>
/// <remarks>
/// Measured against the manifold rather than atmosphere, so it stays constant as load changes on a
/// vehicle with a vacuum-referenced regulator — which is exactly what makes a *drifting* value here
/// worth acting on.
/// </remarks>
public class FuelRailPressureCommand() : ObdCommand<double>(0x01, 0x22)
{
    protected override double ParseData(byte[] data)
    {
        if (data.Length < 2)
            throw new ObdException("Fuel rail pressure response requires 2 data bytes");

        return 0.079 * ((data[0] << 8) | data[1]);
    }
}

/// <summary>
/// Fuel Rail Gauge Pressure (Mode 01, PID 0x23) - Returns direct-injection rail pressure in kPa
/// </summary>
/// <remarks>
/// The diesel and petrol-direct-injection PID, and the one worth having: 10 kPa per bit up to 655 MPa,
/// because a common-rail diesel runs 25-250 MPa. Rail pressure failing to reach its target under load
/// is the primary signature of a tiring high-pressure pump or a leaking injector, and it is visible
/// here well before a code sets.
///
/// <para>
/// <see cref="FuelRailAbsolutePressureCommand"/> (PID 0x59) uses an identical scale but is measured
/// against vacuum rather than atmosphere, so the two differ by roughly one atmosphere — immaterial at
/// 200 MPa, and not immaterial at 300 kPa.
/// </para>
/// </remarks>
public class FuelRailGaugePressureCommand() : ObdCommand<int>(0x01, 0x23)
{
    protected override int ParseData(byte[] data)
    {
        if (data.Length < 2)
            throw new ObdException("Fuel rail gauge pressure response requires 2 data bytes");

        return 10 * ((data[0] << 8) | data[1]);
    }
}

/// <summary>
/// Fuel Rail Absolute Pressure (Mode 01, PID 0x59) - Returns rail pressure in kPa, measured against vacuum
/// </summary>
/// <remarks>
/// The absolute counterpart to <see cref="FuelRailGaugePressureCommand"/> — same 10 kPa per bit, and
/// about one atmosphere higher for the same physical pressure.
/// </remarks>
public class FuelRailAbsolutePressureCommand() : ObdCommand<int>(0x01, 0x59)
{
    protected override int ParseData(byte[] data)
    {
        if (data.Length < 2)
            throw new ObdException("Fuel rail absolute pressure response requires 2 data bytes");

        return 10 * ((data[0] << 8) | data[1]);
    }
}
