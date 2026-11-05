using Shiny.Obd;
using Shiny.Obd.Commands;

namespace Sample.Maui;

/// <summary>A coloured band on a gauge face - the redline, the low-fuel warning.</summary>
public sealed record GaugeBand(double Start, double End, Color Color);

/// <summary>
/// One reading the dashboard can put on a dial: the command that fetches it, and the scale it is drawn
/// against.
/// </summary>
public sealed record GaugeDefinition
{
    /// <summary>Stable id - this is what gets persisted, so do not rename one that has shipped.</summary>
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Units { get; init; }
    public double Minimum { get; init; }
    public required double Maximum { get; init; }
    public double MajorTickInterval { get; init; }
    public int MinorTicksPerMajor { get; init; } = 1;
    public int ReadoutDecimals { get; init; }
    public IReadOnlyList<GaugeBand> Bands { get; init; } = [];
    public required Func<IObdConnection, CancellationToken, Task<double>> Read { get; init; }
}

/// <summary>
/// Every mode 01 reading worth a needle. Status words, counters and strings are left to the command
/// sweep - a dial is for a value that moves.
/// </summary>
public static class GaugeCatalog
{
    public const int MaxSelected = 8;

    static readonly Color Cold = Color.FromArgb("#38BDF8");
    static readonly Color Normal = Color.FromArgb("#22C55E");
    static readonly Color Warn = Color.FromArgb("#F59E0B");
    static readonly Color Danger = Color.FromArgb("#EF4444");

    /// <summary>The six the dashboard showed before gauges were selectable.</summary>
    public static readonly string[] DefaultKeys = ["speed", "rpm", "coolant", "throttle", "fuel", "load"];

    public static IReadOnlyList<GaugeDefinition> All { get; } =
    [
        new()
        {
            Key = "speed", Title = "SPEED", Units = "km/h",
            Maximum = 240, MajorTickInterval = 40,
            Read = Of(StandardCommands.VehicleSpeed)
        },
        new()
        {
            // Scaled 0-8 rather than 0-8000: the tick labels stay legible on a small dial.
            Key = "rpm", Title = "RPM", Units = "x1000 r/min",
            Maximum = 8, MajorTickInterval = 1, ReadoutDecimals = 1,
            Bands = [new(5.5, 6.5, Warn), new(6.5, 8, Danger)],
            Read = Of(StandardCommands.EngineRpm, x => x / 1000.0)
        },
        new()
        {
            Key = "coolant", Title = "COOLANT", Units = "°C",
            Maximum = 140, MajorTickInterval = 20,
            Bands = [new(0, 60, Cold), new(60, 105, Normal), new(105, 140, Danger)],
            Read = Of(StandardCommands.CoolantTemperature)
        },
        new()
        {
            Key = "throttle", Title = "THROTTLE", Units = "%",
            Maximum = 100, MajorTickInterval = 25,
            Read = Of(StandardCommands.ThrottlePosition)
        },
        new()
        {
            Key = "fuel", Title = "FUEL", Units = "%",
            Maximum = 100, MajorTickInterval = 25,
            Bands = [new(0, 15, Danger)],
            Read = Of(StandardCommands.FuelLevel)
        },
        new()
        {
            Key = "load", Title = "LOAD", Units = "%",
            Maximum = 100, MajorTickInterval = 25,
            Read = Of(StandardCommands.CalculatedEngineLoad)
        },
        new()
        {
            Key = "pedal", Title = "PEDAL", Units = "%",
            Maximum = 100, MajorTickInterval = 25,
            Read = Of(StandardCommands.RelativeAcceleratorPedalPosition)
        },
        new()
        {
            Key = "throttle-actuator", Title = "THR ACTUATOR", Units = "%",
            Maximum = 100, MajorTickInterval = 25,
            Read = Of(StandardCommands.CommandedThrottleActuator)
        },
        new()
        {
            Key = "intake-air", Title = "INTAKE AIR", Units = "°C",
            Minimum = -40, Maximum = 80, MajorTickInterval = 20,
            Read = Of(StandardCommands.IntakeAirTemperature)
        },
        new()
        {
            Key = "ambient", Title = "AMBIENT", Units = "°C",
            Minimum = -40, Maximum = 60, MajorTickInterval = 20,
            Read = Of(StandardCommands.AmbientAirTemperature)
        },
        new()
        {
            Key = "oil", Title = "OIL TEMP", Units = "°C",
            Maximum = 160, MajorTickInterval = 40,
            Bands = [new(130, 160, Danger)],
            Read = Of(StandardCommands.EngineOilTemperature)
        },
        new()
        {
            // Under 11.5 V with the engine running is a charging fault; over 14.8 V is overcharging.
            Key = "voltage", Title = "BATTERY", Units = "V",
            Minimum = 8, Maximum = 16, MajorTickInterval = 2, ReadoutDecimals = 1,
            Bands = [new(8, 11.5, Danger), new(14.8, 16, Warn)],
            Read = Of(StandardCommands.ControlModuleVoltage)
        },
        new()
        {
            Key = "maf", Title = "MAF", Units = "g/s",
            Maximum = 250, MajorTickInterval = 50, ReadoutDecimals = 1,
            Read = Of(StandardCommands.MassAirFlow)
        },
        new()
        {
            Key = "map", Title = "MAP", Units = "kPa",
            Maximum = 250, MajorTickInterval = 50,
            Read = Of(StandardCommands.IntakeManifoldPressure)
        },
        new()
        {
            Key = "baro", Title = "BARO", Units = "kPa",
            Minimum = 60, Maximum = 110, MajorTickInterval = 10,
            Read = Of(StandardCommands.BarometricPressure)
        },
        new()
        {
            Key = "fuel-rate", Title = "FUEL RATE", Units = "L/h",
            Maximum = 30, MajorTickInterval = 5, ReadoutDecimals = 1,
            Read = Of(StandardCommands.EngineFuelRate)
        },
        new()
        {
            Key = "fuel-pressure", Title = "FUEL PRESS", Units = "kPa",
            Maximum = 765, MajorTickInterval = 150,
            Read = Of(StandardCommands.FuelPressure)
        },
        new()
        {
            // The full PID range is -100..99.2 %, but a healthy engine lives inside ±10 and one past ±25
            // has already set a code - a dial that wide would barely twitch.
            Key = "stft1", Title = "STFT B1", Units = "%",
            Minimum = -25, Maximum = 25, MajorTickInterval = 10, ReadoutDecimals = 1,
            Bands = [new(-25, -10, Warn), new(10, 25, Warn)],
            Read = Of(FuelTrimCommand.ShortTermBank1())
        },
        new()
        {
            Key = "ltft1", Title = "LTFT B1", Units = "%",
            Minimum = -25, Maximum = 25, MajorTickInterval = 10, ReadoutDecimals = 1,
            Bands = [new(-25, -10, Warn), new(10, 25, Warn)],
            Read = Of(FuelTrimCommand.LongTermBank1())
        },
        new()
        {
            Key = "timing", Title = "TIMING", Units = "° BTDC",
            Minimum = -20, Maximum = 60, MajorTickInterval = 20, ReadoutDecimals = 1,
            Read = Of(StandardCommands.TimingAdvance)
        },
        new()
        {
            Key = "lambda", Title = "LAMBDA", Units = "λ cmd",
            Minimum = 0.5, Maximum = 1.5, MajorTickInterval = 0.25, ReadoutDecimals = 2,
            Read = Of(StandardCommands.CommandedAirFuelRatio)
        },
        new()
        {
            Key = "torque", Title = "TORQUE", Units = "%",
            Maximum = 100, MajorTickInterval = 25,
            Read = Of(StandardCommands.ActualEngineTorque)
        },
        new()
        {
            Key = "hybrid", Title = "HV BATTERY", Units = "%",
            Maximum = 100, MajorTickInterval = 25,
            Bands = [new(0, 20, Danger)],
            Read = Of(StandardCommands.HybridBatteryLife)
        },
        new()
        {
            Key = "ethanol", Title = "ETHANOL", Units = "%",
            Maximum = 100, MajorTickInterval = 25,
            Read = Of(StandardCommands.EthanolFuelPercent)
        }
    ];

    public static GaugeDefinition? Find(string key) => All.FirstOrDefault(x => x.Key == key);

    static Func<IObdConnection, CancellationToken, Task<double>> Of<T>(IObdCommand<T> command, Func<T, double>? map = null)
        where T : struct, IConvertible
        => async (connection, ct) =>
        {
            var value = await connection.Execute(command, ct);
            return map?.Invoke(value) ?? value.ToDouble(null);
        };
}
