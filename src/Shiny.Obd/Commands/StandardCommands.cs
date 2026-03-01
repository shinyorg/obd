namespace Shiny.Obd.Commands;

/// <summary>
/// Provides singleton instances of all standard OBD-II commands for convenient access.
/// </summary>
public static class StandardCommands
{
    public static VehicleSpeedCommand VehicleSpeed { get; } = new();
    public static EngineRpmCommand EngineRpm { get; } = new();
    public static CoolantTemperatureCommand CoolantTemperature { get; } = new();
    public static ThrottlePositionCommand ThrottlePosition { get; } = new();
    public static FuelLevelCommand FuelLevel { get; } = new();
    public static CalculatedEngineLoadCommand CalculatedEngineLoad { get; } = new();
    public static IntakeAirTemperatureCommand IntakeAirTemperature { get; } = new();
    public static RuntimeSinceStartCommand RuntimeSinceStart { get; } = new();
    public static VinCommand Vin { get; } = new();
}
