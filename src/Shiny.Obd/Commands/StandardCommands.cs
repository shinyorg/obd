namespace Shiny.Obd.Commands;

/// <summary>
/// Provides singleton instances of all standard OBD-II commands for convenient access.
/// </summary>
/// <remarks>
/// Only the commands that need no construction data are here. <see cref="SupportedPidsCommand"/>
/// takes a block PID, <see cref="FuelTrimCommand"/> takes a bank and
/// <see cref="AcceleratorPedalPositionCommand"/> takes a sensor, so build those yourself (the last
/// two have static factories); <see cref="DtcReadCommand"/> and <see cref="ClearDtcCommand"/> carry
/// their own shared instances, and mode 02 readings come from
/// <see cref="ObdCommand{T}.AsFreezeFrame"/> on the mode 01 command.
/// </remarks>
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
    public static OdometerCommand Odometer { get; } = new();
    public static DistanceSinceCodesClearedCommand DistanceSinceCodesCleared { get; } = new();
    public static ControlModuleVoltageCommand ControlModuleVoltage { get; } = new();
    public static MassAirFlowCommand MassAirFlow { get; } = new();
    public static EngineFuelRateCommand EngineFuelRate { get; } = new();
    public static EngineOilTemperatureCommand EngineOilTemperature { get; } = new();
    public static FuelTypeCommand FuelType { get; } = new();
    public static HybridBatteryLifeCommand HybridBatteryLife { get; } = new();
    public static MonitorStatusCommand MonitorStatus { get; } = new();
    public static MonitorStatusThisDriveCycleCommand MonitorStatusThisDriveCycle { get; } = new();
    public static FuelSystemStatusCommand FuelSystemStatus { get; } = new();
    public static IntakeManifoldPressureCommand IntakeManifoldPressure { get; } = new();
    public static BarometricPressureCommand BarometricPressure { get; } = new();
    public static TimingAdvanceCommand TimingAdvance { get; } = new();
    public static AmbientAirTemperatureCommand AmbientAirTemperature { get; } = new();
    public static RelativeAcceleratorPedalPositionCommand RelativeAcceleratorPedalPosition { get; } = new();
    public static CommandedThrottleActuatorCommand CommandedThrottleActuator { get; } = new();
    public static DistanceWithMilOnCommand DistanceWithMilOn { get; } = new();
    public static TimeRunWithMilOnCommand TimeRunWithMilOn { get; } = new();
    public static TimeSinceCodesClearedCommand TimeSinceCodesCleared { get; } = new();
    public static CalibrationIdCommand CalibrationId { get; } = new();
    public static CommandedAirFuelRatioCommand CommandedAirFuelRatio { get; } = new();
    public static CommandedEgrCommand CommandedEgr { get; } = new();
    public static EgrErrorCommand EgrError { get; } = new();
    public static CommandedEvaporativePurgeCommand CommandedEvaporativePurge { get; } = new();
    public static EvapVaporPressureCommand EvapVaporPressure { get; } = new();
    public static AbsoluteEvapVaporPressureCommand AbsoluteEvapVaporPressure { get; } = new();
    public static EvapVaporPressureWideRangeCommand EvapVaporPressureWideRange { get; } = new();
    public static DriverDemandTorqueCommand DriverDemandTorque { get; } = new();
    public static ActualEngineTorqueCommand ActualEngineTorque { get; } = new();
    public static ReferenceTorqueCommand ReferenceTorque { get; } = new();
    public static EnginePercentTorqueDataCommand EnginePercentTorqueData { get; } = new();
    public static FuelPressureCommand FuelPressure { get; } = new();
    public static FuelRailPressureCommand FuelRailPressure { get; } = new();
    public static FuelRailGaugePressureCommand FuelRailGaugePressure { get; } = new();
    public static FuelRailAbsolutePressureCommand FuelRailAbsolutePressure { get; } = new();
    public static EthanolFuelPercentCommand EthanolFuelPercent { get; } = new();
    public static AbsoluteLoadValueCommand AbsoluteLoadValue { get; } = new();
    public static WarmUpsSinceCodesClearedCommand WarmUpsSinceCodesCleared { get; } = new();
    public static RelativeThrottlePositionCommand RelativeThrottlePosition { get; } = new();
    public static FuelInjectionTimingCommand FuelInjectionTiming { get; } = new();
    public static EngineRunTimeCommand EngineRunTime { get; } = new();
    public static ObdStandardsCommand ObdStandards { get; } = new();
    public static CalibrationVerificationNumberCommand CalibrationVerificationNumber { get; } = new();
    public static EcuNameCommand EcuName { get; } = new();
}
