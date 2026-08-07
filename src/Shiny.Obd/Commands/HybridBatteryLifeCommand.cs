namespace Shiny.Obd.Commands;

/// <summary>
/// Hybrid/EV Battery Pack Remaining Life (Mode 01, PID 0x5B) - Returns a percentage (0 to 100)
/// Formula: A * 100 / 255
/// </summary>
/// <remarks>
/// The only battery-health figure on standard OBD-II, and a state-of-health estimate the ECU
/// maintains rather than a charge level. Absent on every non-hybrid, so check
/// <see cref="SupportedPidsCommand"/> first — a pack at 0% and a vehicle with no pack must not
/// look the same to a caller.
/// </remarks>
public class HybridBatteryLifeCommand() : ObdCommand<double>(0x01, 0x5B)
{
    protected override double ParseData(byte[] data)
    {
        if (data.Length < 1)
            throw new ObdException("Hybrid battery life response requires 1 data byte");

        return data[0] * 100.0 / 255.0;
    }
}
