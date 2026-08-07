namespace Shiny.Obd.Commands;

/// <summary>
/// Fuel Type (Mode 01, PID 0x51) - Returns a single byte indexing the SAE J1979 fuel type table
/// </summary>
/// <remarks>
/// Read this once per connection rather than polling it: a vehicle does not change what it burns
/// between ticks. It is the one powertrain fact that comes off the bus rather than out of a VIN
/// registry, which matters on a rebadged or grey-import vehicle where the ECU is in the car in
/// front of you and the registry is not. Support is patchy (absent on plenty of pre-2010 vehicles),
/// and it says nothing about displacement, cylinders or drivetrain — none of which exists anywhere
/// on OBD-II. Pass the result to <see cref="FuelTypes.Describe(byte)"/> for a human-readable name.
/// </remarks>
public class FuelTypeCommand() : ObdCommand<byte>(0x01, 0x51)
{
    protected override byte ParseData(byte[] data)
    {
        if (data.Length < 1)
            throw new ObdException("Fuel type response requires 1 data byte");

        return data[0];
    }
}
