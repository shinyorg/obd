namespace Shiny.Obd.Commands;

/// <summary>
/// Absolute Barometric Pressure (Mode 01, PID 0x33) - Returns pressure in kPa (0 to 255)
/// Formula: A
/// </summary>
/// <remarks>
/// Ambient air pressure as the ECU measures it — around 101 kPa at sea level, falling roughly
/// 12 kPa per 1,000 m. It is the reference that turns
/// <see cref="IntakeManifoldPressureCommand"/> into boost or vacuum, and the correction that stops
/// an airflow estimate reading rich at altitude. Do not treat it as an altimeter: weather moves it
/// by 5 kPa or so, which is several hundred metres of apparent height.
/// </remarks>
public class BarometricPressureCommand() : ObdCommand<int>(0x01, 0x33)
{
    protected override int ParseData(byte[] data)
    {
        if (data.Length < 1)
            throw new ObdException("Barometric pressure response requires 1 data byte");

        return data[0];
    }
}
