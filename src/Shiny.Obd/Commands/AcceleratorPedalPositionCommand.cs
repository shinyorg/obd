namespace Shiny.Obd.Commands;

/// <summary>
/// Accelerator Pedal Position (Mode 01, PIDs 0x49/0x4A/0x4B) - Returns a percentage (0 to 100)
/// Formula: (A * 100) / 255
/// </summary>
/// <remarks>
/// This is the <b>driver's input</b>, which is what makes it worth reading separately from
/// <see cref="ThrottlePositionCommand"/> (PID 0x11). That one is absolute throttle plate position —
/// the drive-by-wire system's <i>output</i> — and it carries a closed-pedal floor of 12-18% that
/// varies by vehicle, so anything measuring how hard a car is being driven from PID 0x11 has to
/// work out that floor for itself first. Pedal position needs no such correction.
/// <para>
/// D, E and F are separate sensors on the same pedal (they are redundant for safety, and read
/// close to each other but rarely identically). Most vehicles report D and E; F is uncommon.
/// <see cref="RelativeAcceleratorPedalPositionCommand"/> (PID 0x5A) is the learned, normalised
/// version and is the better choice where it is supported.
/// </para>
/// </remarks>
public class AcceleratorPedalPositionCommand(byte pid) : ObdCommand<double>(0x01, pid)
{
    /// <summary>Accelerator pedal position sensor D (PID 0x49).</summary>
    public static AcceleratorPedalPositionCommand D() => new(0x49);

    /// <summary>Accelerator pedal position sensor E (PID 0x4A).</summary>
    public static AcceleratorPedalPositionCommand E() => new(0x4A);

    /// <summary>Accelerator pedal position sensor F (PID 0x4B).</summary>
    public static AcceleratorPedalPositionCommand F() => new(0x4B);

    protected override double ParseData(byte[] data)
    {
        if (data.Length < 1)
            throw new ObdException("Accelerator pedal position response requires 1 data byte");

        return data[0] * 100.0 / 255.0;
    }
}
