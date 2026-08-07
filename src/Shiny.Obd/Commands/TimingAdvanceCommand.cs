namespace Shiny.Obd.Commands;

/// <summary>
/// Timing Advance (Mode 01, PID 0x0E) - Returns degrees before top dead centre (-64 to 63.5)
/// Formula: (A / 2) - 64
/// </summary>
/// <remarks>
/// How far ahead of top dead centre the ECU is firing cylinder 1. Negative is retard. The value on
/// its own means little — advance varies constantly with load, RPM and temperature — but a car
/// that <i>persistently</i> runs less advance than it used to at comparable load is pulling timing,
/// which is what a knock sensor does on poor fuel, carbon build-up or a developing heat problem.
/// Trend it against this vehicle's own history rather than against any absolute figure.
/// </remarks>
public class TimingAdvanceCommand() : ObdCommand<double>(0x01, 0x0E)
{
    protected override double ParseData(byte[] data)
    {
        if (data.Length < 1)
            throw new ObdException("Timing advance response requires 1 data byte");

        return (data[0] / 2.0) - 64.0;
    }
}
