namespace Shiny.Obd.Commands;

/// <summary>
/// Commanded Throttle Actuator (Mode 01, PID 0x4C) - Returns a percentage (0 to 100)
/// Formula: (A * 100) / 255
/// </summary>
/// <remarks>
/// What the ECU is <i>asking</i> the throttle plate to do, against
/// <see cref="ThrottlePositionCommand"/>'s report of where the plate actually is. A persistent gap
/// between the two is the signature of a sticking or fouled throttle body.
/// </remarks>
public class CommandedThrottleActuatorCommand() : ObdCommand<double>(0x01, 0x4C)
{
    protected override double ParseData(byte[] data)
    {
        if (data.Length < 1)
            throw new ObdException("Commanded throttle actuator response requires 1 data byte");

        return data[0] * 100.0 / 255.0;
    }
}
