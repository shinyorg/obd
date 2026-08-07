namespace Shiny.Obd.Commands;

/// <summary>
/// Intake Manifold Absolute Pressure (Mode 01, PID 0x0B) - Returns pressure in kPa (0 to 255)
/// Formula: A
/// </summary>
/// <remarks>
/// The speed-density counterpart to <see cref="MassAirFlowCommand"/>: a vehicle that reports no MAF
/// almost always reports MAP, and airflow can be estimated from it with RPM and intake air
/// temperature. It is <i>absolute</i> pressure, so subtract
/// <see cref="BarometricPressureCommand"/> to get boost or vacuum relative to the air outside —
/// at sea level a warm idle sits near 30 kPa and wide-open throttle approaches ambient.
/// </remarks>
public class IntakeManifoldPressureCommand() : ObdCommand<int>(0x01, 0x0B)
{
    protected override int ParseData(byte[] data)
    {
        if (data.Length < 1)
            throw new ObdException("Intake manifold pressure response requires 1 data byte");

        return data[0];
    }
}
