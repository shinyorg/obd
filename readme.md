# Shiny.Obd

[![NuGet](https://img.shields.io/nuget/v/Shiny.Obd.svg?style=flat-square)](https://www.nuget.org/packages/Shiny.Obd/)

A .NET library for communicating with vehicles through OBD-II (On-Board Diagnostics) adapters. Supports ELM327 and OBDLink (STN) adapters over pluggable transports — Bluetooth LE, WiFi and serial (USB/UART) ship in the box.

[Documentation](https://shinylib.net/client/obd)

## Features

- **Command-object pattern** — OBD commands are objects, not methods. Pass built-in commands or create your own for custom PIDs.
- **Generic return types** — each command declares its return type (`int`, `double`, `string`, `TimeSpan`, etc.) with compile-time safety.
- **Pluggable transports** — `IObdTransport` abstracts the communication channel. BLE, WiFi and serial (USB/UART) ship in the box; add anything else with one interface.
- **WiFi works everywhere** — a WiFi adapter is a plain TCP socket, so `Shiny.Obd.Wifi` behaves identically on iOS, Android, Windows, Linux and macOS. No platform package, no pairing, no BLE stack.
- **Adapter auto-detection** — detects ELM327 vs OBDLink (STN) adapters via ATI and runs the appropriate initialization sequence.
- **Adapter profiles** — `IObdAdapterProfile` lets you define custom init sequences. Built-in profiles for ELM327 and OBDLink.
- **Multi-frame CAN responses** — the byte-count line and per-frame `N:` index an ELM327 prints for a large reply (the VIN, or mode 03 with three or more codes) are treated as framing and discarded, with spaced and unspaced hex both accepted.
- **Task-based async** — fully async/await throughout, no Reactive Extensions required in consuming code.
- **60+ standard commands included** — speed, RPM, temperatures, pressures, throttle and pedal position, fuel level, trims, rate and system status, timing advance, engine load, odometer, distances and timers, VIN, calibration IDs, hybrid battery life and more.
- **Oxygen sensors, narrowband and wideband** — the measurement behind the fuel trim. Sensor presence and layout, narrowband voltage with its associated trim, and wideband lambda with either voltage or pump current.
- **EGR and EVAP** — commanded position and error for EGR, purge command and all three vapour-pressure encodings for EVAP. The two most common causes of a check engine light.
- **Torque and power** — actual and demanded torque against the engine's reference figure, with `EnginePower` turning the percentages into newton-metres, kilowatts and horsepower.
- **Mode 06 on-board test results** — the actual measurement each emissions monitor took and the limits it was judged against, fully scaled through the SAE unit-and-scaling table, with `BandPosition` showing how close a passing test is to failing.
- **Supported-PID discovery** — `SupportedPidsCommand` reads the mode 01 bitmask blocks so you only ever query readings the vehicle in front of you actually reports.
- **Diagnostic trouble codes** — read stored, pending and permanent codes (modes 03/07/0A) as SAE J2012 strings, and clear them (mode 04). CAN and pre-CAN response framing are both handled.
- **Emissions monitor readiness** — the full mode 01 PID 01/41 bit layout decoded for both spark and compression ignition, with `IsReadyForInspection` answering the question an emissions test actually asks.
- **Freeze frames** — `AsFreezeFrame()` on any mode 01 command reads the same PID out of the snapshot the ECU stored when a code was set, so you get the conditions at the moment of the fault rather than the conditions now.
- **VIN decoding** — `IVinDecoder` turns the VIN off the ECU into make, model, year and the engine/drivetrain/body a registry knows about. NHTSA vPIC ships built in (free, keyless); register your own provider with one line.
- **Test without a car** — the [sample app](samples/Sample.Maui) is also an adapter. It hosts an ELM327-compatible OBD-II bus over BLE *and* TCP, with every PID, trouble code and readiness flag set from a UI. See [Adapter Emulator](#adapter-emulator-sample).

## Projects

| Package | Target | Description |
|---------|--------|-------------|
| `Shiny.Obd` | `net10.0` | Core library — commands, connection, transport abstraction |
| `Shiny.Obd.Ble` | `net10.0` | BLE transport using [Shiny.BluetoothLE](https://github.com/shinyorg/shiny) |
| `Shiny.Obd.Serial` | `net10.0` | Serial (USB/UART) transport — Windows, Linux, macOS, Mac Catalyst |
| `Shiny.Obd.Wifi` | `net10.0` | WiFi (TCP) transport — every platform, including iOS and Android |

## Quick Start

### 1. Install packages

```xml
<!-- Core (always needed) -->
<PackageReference Include="Shiny.Obd" />

<!-- BLE transport -->
<PackageReference Include="Shiny.Obd.Ble" />

<!-- Serial (USB/UART) transport -->
<PackageReference Include="Shiny.Obd.Serial" />

<!-- WiFi (TCP) transport -->
<PackageReference Include="Shiny.Obd.Wifi" />
```

### Registration

```csharp
// WiFi — every platform, including iOS and Android
services.AddShinyObdWifi();

// Serial — Windows, Linux, macOS, Mac Catalyst
services.AddShinyObdSerial(config => config.PortNameFilter = "OBDLink");

// BLE — every platform Shiny.BluetoothLE supports
services.AddShinyObdBluetoothLE();
```

Each registers `IObdTransport`, `IObdConnection` and `IObdDeviceScanner` as singletons — an adapter is
one physical resource. Registration uses `TryAdd`, so calling more than one leaves whichever ran first
in place rather than silently swapping the transport; if you want a **fallback chain** across
transports, construct them yourself instead of registering several.

On iOS and Android `AddShinyObdBluetoothLE()` registers the BLE manager for you. **Everywhere else
you also call `AddBluetoothLE()`** from your platform package — order does not matter:

```csharp
services.AddBluetoothLE();          // Shiny.BluetoothLE.Linux / .Blazor / .BluetoothLE
services.AddShinyObdBluetoothLE();
```

That split is a hard constraint, not an omission: `Shiny.BluetoothLE.Linux` and
`Shiny.BluetoothLE.Blazor` both ship a `net10.0` assembly declaring
`Shiny.AddBluetoothLE(IServiceCollection)`, so a package referencing both would make every call to it
ambiguous (CS0121). Only the app knows which platform it is running on. Forget the call and you get a
`ObdException` naming the exact package to add, rather than a bare DI resolution failure.

### 2. Connect and query

```csharp
using Shiny.Obd;
using Shiny.Obd.Ble;
using Shiny.Obd.Commands;

// Create BLE transport (scans for adapter automatically)
var transport = new BleObdTransport(bleManager, new BleObdConfiguration
{
    DeviceNameFilter = "OBDLink" // optional: filter by adapter name
});

// Create connection (auto-detects adapter type)
var connection = new ObdConnection(transport);
await connection.Connect();

// Execute commands
var speed = await connection.Execute(StandardCommands.VehicleSpeed);    // int (km/h)
var rpm = await connection.Execute(StandardCommands.EngineRpm);        // int
var vin = await connection.Execute(StandardCommands.Vin);              // string

Console.WriteLine($"Speed: {speed} km/h, RPM: {rpm}, VIN: {vin}");
```

## Architecture

```
┌─────────────────────────────────────────────────┐
│                 Your App                        │
│   await connection.Execute(StandardCommands.*)  │
└──────────────────────┬──────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────┐
│              ObdConnection                      │
│  • Adapter detection (ATI probe)                │
│  • Profile-based initialization                 │
│  • ELM327 response parsing (hex → bytes)        │
│  • Error handling (NO DATA, UNABLE TO CONNECT)  │
└──────────────────────┬──────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────┐
│             IObdTransport                       │
│  Pluggable transport layer                      │
│  ┌──────────────────┐  ┌────────────────────┐   │
│  │  BleObdTransport │  │ SerialObdTransport │   │
│  │    (Shiny BLE)   │  │    (USB / UART)    │   │
│  └──────────────────┘  └────────────────────┘   │
│  ┌──────────────────┐  ┌────────────────────┐   │
│  │ WifiObdTransport │  │      your own      │   │
│  │       (TCP)      │  │         …          │   │
│  └──────────────────┘  └────────────────────┘   │
└─────────────────────────────────────────────────┘
```

## Commands

### Standard Commands

All standard commands are available as singletons via `StandardCommands`:

| Command | Mode | PID | Return Type | Unit |
|---------|------|-----|-------------|------|
| `VehicleSpeed` | 01 | 0D | `int` | km/h |
| `EngineRpm` | 01 | 0C | `int` | RPM |
| `CoolantTemperature` | 01 | 05 | `int` | °C |
| `ThrottlePosition` | 01 | 11 | `double` | % |
| `FuelLevel` | 01 | 2F | `double` | % |
| `CalculatedEngineLoad` | 01 | 04 | `double` | % |
| `IntakeAirTemperature` | 01 | 0F | `int` | °C |
| `RuntimeSinceStart` | 01 | 1F | `TimeSpan` | — |
| `Vin` | 09 | 02 | `string` | — |
| `Odometer` | 01 | A6 | `double` | km |
| `DistanceSinceCodesCleared` | 01 | 31 | `int` | km |
| `ControlModuleVoltage` | 01 | 42 | `double` | V |
| `MassAirFlow` | 01 | 10 | `double` | g/s |
| `EngineFuelRate` | 01 | 5E | `double` | L/h |
| `EngineOilTemperature` | 01 | 5C | `int` | °C |
| `FuelType` | 01 | 51 | `byte` | J1979 code |
| `HybridBatteryLife` | 01 | 5B | `double` | % |
| `MonitorStatus` | 01 | 01 | `MonitorStatus` | MIL, code count, readiness |
| `MonitorStatusThisDriveCycle` | 01 | 41 | `MonitorStatus` | readiness, this cycle |
| `FuelSystemStatus` | 01 | 03 | `FuelSystemStatus` | loop state |
| `IntakeManifoldPressure` | 01 | 0B | `int` | kPa |
| `BarometricPressure` | 01 | 33 | `int` | kPa |
| `TimingAdvance` | 01 | 0E | `double` | ° BTDC |
| `AmbientAirTemperature` | 01 | 46 | `int` | °C |
| `RelativeAcceleratorPedalPosition` | 01 | 5A | `double` | % |
| `CommandedThrottleActuator` | 01 | 4C | `double` | % |
| `DistanceWithMilOn` | 01 | 21 | `int` | km |
| `TimeRunWithMilOn` | 01 | 4D | `TimeSpan` | minutes |
| `TimeSinceCodesCleared` | 01 | 4E | `TimeSpan` | minutes |
| `CalibrationId` | 09 | 04 | `IReadOnlyList<string>` | — |
| `CommandedAirFuelRatio` | 01 | 44 | `double` | lambda |
| `CommandedEgr` | 01 | 2C | `double` | % |
| `EgrError` | 01 | 2D | `double` | % |
| `CommandedEvaporativePurge` | 01 | 2E | `double` | % |
| `EvapVaporPressure` | 01 | 32 | `double` | Pa (signed) |
| `AbsoluteEvapVaporPressure` | 01 | 53 | `double` | kPa |
| `EvapVaporPressureWideRange` | 01 | 54 | `double` | Pa (signed) |
| `DriverDemandTorque` | 01 | 61 | `int` | % |
| `ActualEngineTorque` | 01 | 62 | `int` | % |
| `ReferenceTorque` | 01 | 63 | `int` | N·m |
| `EnginePercentTorqueData` | 01 | 64 | `EnginePercentTorqueData` | % |
| `FuelPressure` | 01 | 0A | `int` | kPa |
| `FuelRailPressure` | 01 | 22 | `double` | kPa |
| `FuelRailGaugePressure` | 01 | 23 | `int` | kPa |
| `FuelRailAbsolutePressure` | 01 | 59 | `int` | kPa |
| `EthanolFuelPercent` | 01 | 52 | `double` | % |
| `AbsoluteLoadValue` | 01 | 43 | `double` | % |
| `WarmUpsSinceCodesCleared` | 01 | 30 | `int` | count |
| `RelativeThrottlePosition` | 01 | 45 | `double` | % |
| `FuelInjectionTiming` | 01 | 5D | `double` | ° |
| `EngineRunTime` | 01 | 7F | `EngineRunTime` | — |
| `ObdStandards` | 01 | 1C | `byte` | J1979 code |
| `CalibrationVerificationNumber` | 09 | 06 | `IReadOnlyList<string>` | hex |
| `EcuName` | 09 | 0A | `string` | — |

```csharp
var speed = await connection.Execute(StandardCommands.VehicleSpeed);
var rpm = await connection.Execute(StandardCommands.EngineRpm);
var coolant = await connection.Execute(StandardCommands.CoolantTemperature);
var throttle = await connection.Execute(StandardCommands.ThrottlePosition);
var fuel = await connection.Execute(StandardCommands.FuelLevel);
var load = await connection.Execute(StandardCommands.CalculatedEngineLoad);
var intakeTemp = await connection.Execute(StandardCommands.IntakeAirTemperature);
var runtime = await connection.Execute(StandardCommands.RuntimeSinceStart);
var vin = await connection.Execute(StandardCommands.Vin);

// MIL state and how many confirmed codes are stored
var status = await connection.Execute(StandardCommands.MonitorStatus);
Console.WriteLine($"Check engine: {status.MilOn}, {status.DtcCount} code(s)");

// Fuel type is a J1979 code — FuelTypes turns it into a name, or null when it is
// 0x00 ("not available") or outside the table. Read it once per connection; a vehicle
// does not change what it burns between polls.
var fuelType = FuelTypes.Describe(await connection.Execute(StandardCommands.FuelType));
```

Commands that need construction data are not on `StandardCommands`:

```csharp
// Fuel trim takes a bank — 128 is zero correction, positive means the ECU is adding fuel
var shortTerm = await connection.Execute(FuelTrimCommand.ShortTermBank1());   // 0106
var longTerm = await connection.Execute(FuelTrimCommand.LongTermBank1());     // 0107

// Pedal position takes a sensor. This is the driver's *input* — unlike ThrottlePosition
// (PID 0x11), which is absolute throttle plate position and carries a 12-18% closed floor
var pedal = await connection.Execute(AcceleratorPedalPositionCommand.D());    // 0149

// Oxygen sensors take a 1-8 index; throttle sensors B and C have their own factories
var o2 = await connection.Execute(OxygenSensorVoltageCommand.Sensor(1));      // 0114
var wideband = await connection.Execute(OxygenSensorLambdaCommand.WithCurrent(1));  // 0134
var throttleB = await connection.Execute(AbsoluteThrottlePositionCommand.B()); // 0147

// In-use performance tracking differs by engine type
var ipt = await connection.Execute(InUsePerformanceTrackingCommand.Spark());  // 0908
```

Fuel trims only mean anything in closed loop — in open loop the ECU runs a fixed map with no oxygen
sensor feedback, so a trim figure there says nothing about a leak or a lazy sensor:

```csharp
var fuelSystem = await connection.Execute(StandardCommands.FuelSystemStatus);
if (fuelSystem.IsClosedLoop)
    RecordTrim(await connection.Execute(FuelTrimCommand.ShortTermBank1()));
```

### Emissions Monitor Readiness

`MonitorStatus` decodes the whole of PID 0x01, not just the lamp. `Monitors` lists only the monitors
the vehicle actually supports — one that does not exist on a car has no readiness state worth
showing — and the bit layout differs between spark and compression ignition, which the decoder
selects for you.

```csharp
var status = await connection.Execute(StandardCommands.MonitorStatus);

Console.WriteLine($"MIL: {status.MilOn}, {status.DtcCount} stored code(s)");
Console.WriteLine($"Ignition: {status.Ignition}");

if (status.IsReadyForInspection == false)
    Console.WriteLine($"Still running: {String.Join(", ", status.Incomplete.Select(x => x.Monitor))}");
```

A vehicle whose codes were recently cleared reads not-ready for several drive cycles with nothing
wrong with it — `StandardCommands.TimeSinceCodesCleared` (PID 0x4E) is what tells you which case
you are in. `MonitorStatusThisDriveCycle` (PID 0x41) reports the same monitors for the current
cycle only; its byte A is reserved, so its `MilOn` and `DtcCount` are always empty.

### Freeze Frames (mode 02)

Mode 02 accepts the same PIDs as mode 01 and scales them identically, so there is no separate
command per reading — call `AsFreezeFrame()` on the mode 01 command you already have. What you get
back is the vehicle's state at the instant a code was set, rather than its state now.

```csharp
var causal = await connection.Execute(FreezeFrameCommands.CausalDtc());
if (causal != null)
{
    Console.WriteLine($"{causal} was set at:");
    Console.WriteLine(await connection.Execute(StandardCommands.EngineRpm.AsFreezeFrame()));
    Console.WriteLine(await connection.Execute(StandardCommands.CalculatedEngineLoad.AsFreezeFrame()));
    Console.WriteLine(await connection.Execute(StandardCommands.CoolantTemperature.AsFreezeFrame()));
}
```

> ⚠️ **Always check `CausalDtc` first.** When it answers null there is no stored snapshot, and every
> other mode 02 reading is meaningless rather than merely absent — the frame is zero-filled, so an
> engine load of 0% and a coolant temperature of -40 °C come back looking like measurements.

### Supported PIDs

Querying an unsupported PID just returns NO DATA, so probe the bitmask blocks up front and offer
only the readings the vehicle actually answers. Each block reports the 32 PIDs that follow it.

```csharp
var supported = new HashSet<byte>();
foreach (var block in SupportedPidsCommand.BlockPids)   // 00, 20, 40, 60, 80, A0, C0
{
    var pids = await connection.Execute(new SupportedPidsCommand(block));
    foreach (var pid in pids)
        supported.Add(pid);
}

if (supported.Contains(0xA6))
    odometerKm = await connection.Execute(StandardCommands.Odometer);
```

An unsupported reading should surface to your users as *missing*, not as zero — the odometer PID is
absent on most vehicles, and hybrid battery life is absent on every vehicle without a pack.

### Diagnostic Trouble Codes

`DtcReadCommand` returns SAE J2012 code strings (`"P0301"`). The CAN and pre-CAN response framings
are distinguished by payload parity, so the same command works across protocols.

```csharp
var stored = await connection.Execute(DtcReadCommand.Stored);        // mode 03 — these turn the MIL on
var pending = await connection.Execute(DtcReadCommand.Pending);      // mode 07 — current/last drive cycle
var permanent = await connection.Execute(DtcReadCommand.Permanent);  // mode 0A — only the ECU clears these

// Mode 04 also resets the emissions readiness monitors, which then take several drive cycles
// to re-run. Only ever issue this from an explicitly confirmed user action.
var cleared = await connection.Execute(ClearDtcCommand.Instance);
```

### Oxygen Sensors

Fuel trim tells you the ECU's correction; the oxygen sensor tells you the measurement causing it. The
pair is what separates a genuine mixture problem from a failing sensor.

**Read the layout first, and not just to skip absent sensors.** A vehicle answers either PID `0x13`
(two banks of four) or PID `0x1D` (four banks of two), never both — and which one it answers changes
what every sensor PID *means*:

```csharp
var layout = await connection.Execute(OxygenSensorsPresentCommand.TwoBanks());   // or .FourBanks()

foreach (var sensor in layout.Sensors)
{
    var reading = await connection.Execute(OxygenSensorVoltageCommand.Sensor(sensor.SensorIndex));
    Console.WriteLine($"{sensor} — {reading.Volts:F3} V, trim {reading.ShortTermFuelTrim:F1}%");
}
```

PID `0x16` is bank 1 sensor 3 under the first layout and bank 2 sensor 1 under the second. Label a
reading from the wrong one and you send someone to replace the downstream sensor on the wrong bank,
which is why `layout.Position(index)` exists rather than leaving the mapping to callers.

Narrowband sensors answer `OxygenSensorVoltageCommand` (PIDs `0x14`–`0x1B`); widebands answer
`OxygenSensorLambdaCommand` (`0x24`–`0x2B` with voltage, or `0x34`–`0x3B` with pump current). Probe
with `SupportedPidsCommand` — the two report voltages that are **not** comparable.

```csharp
var wide = await connection.Execute(OxygenSensorLambdaCommand.WithCurrent(1));
Console.WriteLine($"Lambda {wide.Lambda:F3} ({wide.PetrolAirFuelRatio:F1}:1), {wide.Milliamps:F1} mA");

// The target, for comparison against the measurement above
var commanded = await connection.Execute(StandardCommands.CommandedAirFuelRatio);
```

> A healthy **upstream** narrowband oscillates roughly 0.1–0.9 V several times a second once hot — a
> reading parked mid-range means a lazy or cold sensor, not a perfect mixture. A **downstream** sensor
> should sit steady around 0.6–0.7 V; when it starts mirroring the upstream swing, the catalyst has
> stopped storing oxygen. One sample is worth very little: read the shape over several seconds.

`ShortTermFuelTrim` is **null** when the vehicle marks the sensor as not used in the trim calculation.
That marker (`0xFF`) scales to +99.2%, which would otherwise land on a graph looking like a wildly
rich correction.

### EGR and EVAP

The two most common causes of a check engine light.

```csharp
// EGR — commanded alone says only what was asked for; the error says whether it happened
var commanded = await connection.Execute(StandardCommands.CommandedEgr);
var error = await connection.Execute(StandardCommands.EgrError);

// EVAP — purge command plus tank pressure distinguishes a real leak from a valve that won't seal
var purge = await connection.Execute(StandardCommands.CommandedEvaporativePurge);
var pressure = await connection.Execute(StandardCommands.EvapVaporPressure);   // signed Pa
```

A P0401 (insufficient EGR flow) with 0% commanded is a different fault from the same code with 40%
commanded and a large negative error — the latter is the classic carbon-clogged passage, and is
visible here long before the code sets.

> ⚠️ **Three PIDs are all called some variant of "evap system vapour pressure" and they are not
> interchangeable.** `0x32` is signed pascals (±8 kPa, fine), `0x54` is signed pascals (±32 kPa,
> coarse), and `0x53` is *unsigned kilopascals measured against vacuum*, so ~101 kPa is atmospheric
> rather than zero. Probe with `SupportedPidsCommand` and use whichever the vehicle answers. Never
> convert between them.

### Torque and Power

Mode 01 reports torque as a percentage of a reference figure, so neither PID means anything alone.
`ReferenceTorque` is a constant for the engine — read it once and reuse it rather than paying for it
on every sample:

```csharp
var reference = await connection.Execute(StandardCommands.ReferenceTorque);     // N·m, read once

// Then per sample
var percent = await connection.Execute(StandardCommands.ActualEngineTorque);
var rpm = await connection.Execute(StandardCommands.EngineRpm);

var nm = EnginePower.TorqueNm(percent, reference);
var kw = EnginePower.Kilowatts(percent, reference, rpm);
var hp = EnginePower.MetricHorsepower(percent, reference, rpm);
```

`MetricHorsepower` (PS, 735.5 W) and `MechanicalHorsepower` (hp, 745.7 W) are both offered rather than
one being called "horsepower": they differ by about 1.4%, which is small enough to look like
measurement noise and large enough to make two apps disagree about the same car.

Negative values are normal and mean the engine is being driven rather than driving. This is the
engine's output at the flywheel, before the drivetrain — it is not a chassis dyno and will read higher
than one.

### Mode 06 — On-Board Test Results

The deepest data OBD-II exposes, and the only mode that answers **"how close is this to failing"**.
Everything else reports a state: a code is set or it is not, a monitor is complete or it is not. Mode
06 reports the measurement the monitor actually took and the limits it was judged against.

```csharp
// Discover what the vehicle supports — there are 224 MIDs and an unsupported one returns NO DATA
var supported = new List<byte>();
foreach (var block in MonitorIds.BlockMids)            // 00, 20, 40, 60, 80, A0
    supported.AddRange(await connection.Execute(new OnBoardTestSupportedMidsCommand(block)));

foreach (var mid in supported)
{
    foreach (var test in await connection.Execute(new OnBoardTestCommand(mid)))
    {
        Console.WriteLine(
            $"{test.Monitor ?? $"MID {test.Mid:X2}"} test {test.TestId:X2}: " +
            $"{test.Value:F2} {test.Unit} (limits {test.Minimum:F2}–{test.Maximum:F2}) " +
            $"{(test.Passed == true ? "PASS" : "FAIL")} — {test.BandPosition:P0} of band"
        );
    }
}
```

`BandPosition` is the number this mode exists for. A result that passes tells you nothing about trend;
a result sitting at 95% of its band, compared against the same reading six months ago, is a component
you can schedule rather than wait to fail.

Every value carries a **unit-and-scaling identifier** saying how to decode it — the same 16-bit number
is 0.25 rpm per bit under one identifier and 0.122 mV under another, so decoding without the table
produces numbers that look plausible and are wrong by orders of magnitude. Identifiers at `0x80` and
above are the signed forms, and reading one of those as unsigned turns a small negative measurement
into ~65,535 and a comfortably passing test into a dramatic failure.

When an identifier is outside the standard table, `Value`, `Unit`, `Passed` and `BandPosition` are all
**null** while `RawValue`, `RawMinimum` and `RawMaximum` remain — the raw comparison is still yours to
make, but the library will not guess at signedness on your behalf.

MIDs above `0xDF` are manufacturer-defined, so `Monitor` is null for them. Manufacturers also publish
their own definitions for the standardised ranges (GM's are the best known), which is worth knowing
when a result carries a name from here but a value that only makes sense against their documentation.

> Mode 06 is defined for CAN (ISO 15765-4) vehicles. Pre-CAN protocols used a different, largely
> manufacturer-specific format; on such a vehicle expect an `ObdException` naming that as the likely
> cause rather than wrong numbers.

### ECU Identity and In-Use Performance

```csharp
// Calibration ID and CVN are a pair — the CVN is computed over the calibration itself, so a reflash
// that keeps the same ID still changes it
var ids = await connection.Execute(StandardCommands.CalibrationId);
var cvns = await connection.Execute(StandardCommands.CalibrationVerificationNumber);

// How often each monitor actually runs against how often it could have
var ipt = await connection.Execute(InUsePerformanceTrackingCommand.Spark());   // or .Compression()
foreach (var monitor in ipt.Monitors)
    Console.WriteLine($"{monitor.Monitor}: {monitor.Ratio:P1}");
```

A ratio persistently near zero on a car with no fault means the monitor's enabling conditions are
never met by how that vehicle is driven — short trips, mostly. That is the real explanation behind a
car that will not reach emissions readiness no matter how long it is driven, which `MonitorStatus` can
only report as "still incomplete". A **null** ratio is different again: the denominator is zero, so the
monitor never had the opportunity at all.

### Custom Commands

Implement `IObdCommand<T>` directly for full control, or extend `ObdCommand<T>` for standard Mode/PID commands.

#### Extending ObdCommand\<T\> (standard Mode/PID pattern)

```csharp
// Barometric pressure (Mode 01, PID 0x33) — single byte, value in kPa
public class BarometricPressureCommand : ObdCommand<int>
{
    public BarometricPressureCommand() : base(0x01, 0x33) { }
    protected override int ParseData(byte[] data) => data[0];
}

// Usage
var pressure = await connection.Execute(new BarometricPressureCommand());
```

The `ObdCommand<T>` base class automatically:
- Generates `RawCommand` from Mode + PID (e.g. `"0133"`)
- Validates the response header (mode echo + PID match)
- Strips the 2-byte header before calling your `ParseData`

#### Implementing IObdCommand\<T\> (full control)

```csharp
// Completely custom command with non-standard response format
public class CustomDiagnosticCommand : IObdCommand<string>
{
    public string RawCommand => "2101";  // manufacturer-specific

    public string Parse(byte[] data)
    {
        // You receive ALL response bytes — parse however you need
        return BitConverter.ToString(data);
    }
}
```

## VIN Decoding

The ECU reports its VIN on mode 09 PID 0x02, and a VIN is a licence plate for a *specification* —
make, model, year, and the engine, drivetrain and body a registry knows about. **None of that last
group exists on the OBD-II bus at any PID**, so a registry lookup is the only source.

```csharp
// Startup
services.AddVinDecoder();          // NHTSA vPIC — free, keyless, no registration

// Anywhere
var vin = await connection.Execute(StandardCommands.Vin);
var vehicle = await vinDecoder.Decode(vin);

if (vehicle != null)
    Console.WriteLine($"{vehicle.ModelYear} {vehicle.Make} {vehicle.Model} — {vehicle.EngineDisplacementLitres}L");
```

vPIC is a US federal registry: excellent for North America, thinner elsewhere. Substitute a
commercial provider, a regional registry or an offline table without touching calling code:

```csharp
services.AddVinDecoder<MyRegistryVinDecoder>();
```

`VinVehicle` is provider-neutral and arrives **typed and cleaned** rather than as the strings a
registry sends:

- Numbers are parsed **invariantly** and bounded. Registries use an invariant decimal point, so a
  naive parse under a comma-decimal culture reads `"3.5"` as thirty-five and reports a 35-litre
  engine; out-of-range values are dropped to null rather than passed on.
- The placeholders registries use for an empty string (`"Not Applicable"`, `"Not Available"`,
  `"N/A"`) become null. A blank is an absence, never a claim — and these values commonly end up in
  front of a user or in an AI prompt, where "Unknown" reads as a fact about the car.

> ⚠️ **`IVinDecoder` never throws.** Callers are background enrichment, not features: being out of
> signal is the ordinary case in a vehicle. It returns null rather than guessing, and any
> implementation you register must do the same.

`VinNumber.IsPlausible` is the pure pre-check — 17 characters from the VIN alphabet, with I, O and Q
excluded because they are confusable with 1 and 0. The check digit is deliberately **not** validated:
it is only mandatory in North America, so rejecting a legitimate non-NA VIN would cost more than one
wasted request.

## Adapter Profiles

### Auto-Detection (default)

When you create `ObdConnection(transport)` without a profile, `Connect()` sends ATI to identify the adapter:

| ATI Response Contains | Detected As | Profile Used |
|----------------------|-------------|--------------|
| `"ELM327"` | `ObdAdapterType.Elm327` | `Elm327AdapterProfile` |
| `"STN"` | `ObdAdapterType.ObdLink` | `ObdLinkAdapterProfile` |
| Anything else | `ObdAdapterType.Unknown` | `Elm327AdapterProfile` |

```csharp
var connection = new ObdConnection(transport);
await connection.Connect();

// Check what was detected
Console.WriteLine(connection.DetectedAdapter?.RawIdentifier); // "ELM327 v1.5"
Console.WriteLine(connection.DetectedAdapter?.Type);          // Elm327
```

### Explicit Profile

Skip detection by providing a profile:

```csharp
var connection = new ObdConnection(transport, new ObdLinkAdapterProfile());
await connection.Connect(); // uses OBDLink init, no ATI probe
```

### Built-in Profiles

**Elm327AdapterProfile** — Standard initialization:
```
ATZ    → Reset
ATE0   → Echo off
ATL0   → Linefeed off
ATS1   → Spaces on
ATH0   → Headers off
ATSP0  → Auto protocol
```

**ObdLinkAdapterProfile** — Extends ELM327 with STN-specific optimizations:
```
(all ELM327 commands above)
STFAC  → Reset to factory defaults
ATCAF1 → CAN auto formatting on
```

### Custom Profiles

```csharp
public class MyAdapterProfile : IObdAdapterProfile
{
    public string Name => "MyAdapter";

    public async Task Initialize(IObdConnection connection, CancellationToken ct = default)
    {
        await connection.SendRaw("ATZ", ct);
        await Task.Delay(500, ct);
        await connection.SendRaw("ATE0", ct);
        await connection.SendRaw("ATSP6", ct);  // force CAN 11-bit 500kbaud
        // ... any adapter-specific commands
    }
}
```

## Device Discovery

Before connecting, scan for available OBD adapters with `IObdDeviceScanner`:

```csharp
using Shiny.Obd;
using Shiny.Obd.Ble;

var scanner = new BleObdDeviceScanner(bleManager, new BleObdConfiguration
{
    DeviceNameFilter = "OBD"
});

var cts = new CancellationTokenSource();
await scanner.Scan(device =>
{
    Console.WriteLine($"Found: {device.Name} ({device.Id})");
    // device.NativeDevice is IPeripheral for BLE
}, cts.Token);
```

`DeviceNameFilter` is a case-insensitive substring match against the peripheral's name, falling back to the local name in the advertisement when the peripheral reports none — which is the normal case on iOS while scanning.

The scan is not filtered by `ServiceUuid`. iOS matches a scan filter against the advertisement only, and most ELM327 clones don't advertise their service, so a filtered scan would find nothing there.

Every advertisement seen is logged at `Debug` level before filtering — name, `Peripheral.Name`, id, RSSI and advertised service UUIDs — so an adapter that never reaches your callback can still be identified:

```csharp
builder.Logging.AddDebug().SetMinimumLevel(LogLevel.Debug);
```

Each discovered device is an `ObdDiscoveredDevice` with `Name`, `Id`, and `NativeDevice`. Pass it directly to `BleObdTransport`:

```csharp
var transport = new BleObdTransport(device, new BleObdConfiguration());
var connection = new ObdConnection(transport);
await connection.Connect();
```

### DI Registration

Register BLE OBD services in one call:

```csharp
using Shiny;

builder.Services.AddBluetoothLE(); // Shiny BLE platform registration
builder.Services.AddShinyObdBluetoothLE(new BleObdConfiguration
{
    DeviceNameFilter = "OBD"
});
```

`AddShinyObdBluetoothLE` registers `BleObdConfiguration` and `IObdDeviceScanner` (`BleObdDeviceScanner`). You must also call `AddBluetoothLE()` for platform BLE support.
```

## BLE Transport

### Configuration

```csharp
var config = new BleObdConfiguration
{
    // GATT UUIDs — defaults work for most ELM327 BLE clones
    ServiceUuid = "FFF0",
    ReadCharacteristicUuid = "FFF1",    // notifications (RX from adapter)
    WriteCharacteristicUuid = "FFF2",   // write commands (TX to adapter)

    // Optional: filter scan results by device name
    DeviceNameFilter = "OBDLink",

    // Timeout for a single command response
    CommandTimeout = TimeSpan.FromSeconds(10)
};
```

### Using a Discovered Device

Use `BleObdDeviceScanner` to find adapters, then pass the selected device directly:

```csharp
ObdDiscoveredDevice device = /* from scanner */;
var transport = new BleObdTransport(device, new BleObdConfiguration());
```

### Using a Pre-Scanned Peripheral

If you've already discovered the BLE peripheral (e.g. from a scan UI):

```csharp
IPeripheral peripheral = /* from your scan */;
var transport = new BleObdTransport(peripheral, new BleObdConfiguration());
```

### Auto-Scan

Let the transport scan for the first matching device:

```csharp
IBleManager bleManager = /* from DI */;
var transport = new BleObdTransport(bleManager, new BleObdConfiguration
{
    DeviceNameFilter = "OBDII"
});
```

## WiFi Transport (TCP)

Works with any ELM327-compatible adapter that exposes a raw TCP socket — OBDLink MX Wi-Fi, Veepeak
WiFi, Vgate iCar, and the ESP8266/ESP32-based clones.

A WiFi OBD adapter is a TCP-to-UART bridge: it runs its own access point, you join it, and it hands
you the ELM327's serial stream over a socket. **This is the only transport with no platform story** —
it is a plain socket, so it behaves identically on iOS, Android, Windows, Linux and macOS. Serial
cannot be used on iOS or Android at all, and BLE needs a BLE adapter and pairing.

### Registration

```csharp
// Probes the well-known adapter addresses and the current network's gateway
services.AddShinyObdWifi();

// Or pin it, skipping detection
services.AddShinyObdWifi("192.168.0.10", 35000);
```

### Direct construction

```csharp
var transport = new WifiObdTransport(new WifiObdConfiguration
{
    // Null discovers the adapter; a value here is simply tried first
    Host = null,
    Port = 35000,

    // Validates a candidate with ATI rather than trusting the TCP connect
    AutoDetectEndpoint = true,

    // Send a cheap ATI when idle this long, so the adapter doesn't drop the socket
    KeepAliveInterval = TimeSpan.FromSeconds(20),

    CommandTimeout = TimeSpan.FromSeconds(10)
});

var connection = new ObdConnection(transport);
await connection.Connect();
```

`ConnectedEndpoint` and `DetectedIdentifier` report what was actually reached, which is worth logging
when detection is in play.

### Endpoint detection

**A TCP connect proves nothing.** Anything listening on the address accepts — your router on
192.168.0.1 will happily complete a connect and then never say a word. So detection validates each
candidate with an ATI and only accepts a reply terminated by the `>` prompt, which nothing else on a
home network produces.

Candidates are tried in this order:

| Order | Source | Why |
|---|---|---|
| 1 | `Host`, when set | A configured address costs nothing when it is right |
| 2 | Default gateway of each up interface | These adapters run the AP you joined, so they *are* the gateway |
| 3 | `EndpointCandidates` | `192.168.0.10:35000` (OBDLink/ScanTool and most clones), `192.168.4.1:35000` (stock ESP8266/ESP32 SoftAP), `192.168.1.5`, `10.0.0.10`, and the two port-23 variants |

`WifiObdDeviceScanner` probes the same list and implements `IObdDeviceScanner`, so a "pick your
adapter" UI works over WiFi, serial and BLE without caring which is which.
`ObdDiscoveredDevice.NativeDevice` is a `WifiObdEndpoint`.

```csharp
var scanner = new WifiObdDeviceScanner();
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

await scanner.Scan(device => Console.WriteLine($"{device.Name} at {device.Id}"), cts.Token);
```

Each probe closes its socket before moving on, and that matters: **most of these adapters accept
exactly one TCP client at a time**, so a scanner that lingered would lock out the transport about to
connect for real. For the same reason, don't register two consumers of one adapter.

### Joining the adapter's network is the app's job

The transport connects a socket; it cannot join a WiFi network for you, and the OS will not
necessarily route through one that has no internet.

- **Android** keeps the default route on cellular, so the socket connects to nothing. Pin traffic to
  the adapter's network with `ConnectivityManager.BindProcessToNetwork(network)`, or bind the socket
  itself via `WifiObdConfiguration.ConfigureSocket`.
- **iOS** needs `NSLocalNetworkUsageDescription` in `Info.plist` and the user's consent. A denial is
  silent and looks exactly like a dead adapter.

```csharp
services.AddShinyObdWifi(config => config.ConfigureSocket = socket =>
{
    // e.g. bind to a specific Android.Net.Network, set buffer sizes, enable TCP keep-alives
    socket.ReceiveBufferSize = 8192;
});
```

### No auto-reconnect, on purpose

A dropped socket loses the ELM327's session state — `ATE0`, `ATS1`, the negotiated protocol — because
that state lives in the adapter, not the socket. Silently redialling would hand you a live connection
with echo back on, whose replies parse as garbage rather than failing outright. A lost link surfaces
as `ObdException`; recover by calling `ObdConnection.Connect()` again, which re-initialises the
adapter.

`KeepAliveInterval` is what stops the idle drop happening in the first place — clone firmware
commonly closes a socket idle for 30–60 seconds. A polling loop never notices; an app that connects
and then waits for the user does. The keep-alive sends `ATI`, which the adapter answers itself and
never puts on the vehicle bus, and it skips itself whenever a real command is in flight.

> **Leave `NoDelay` on.** An OBD exchange is a tiny write followed by a tiny reply, which is exactly
> the traffic Nagle's algorithm delays. With it enabled you pay tens of milliseconds on every PID
> read — the difference between a usable live-data gauge and a sluggish one.

## Serial Transport (USB / UART)

Works with any ELM327-compatible adapter that presents as a serial port — OBDLink SX/EX, ELM327
clones on CH340/FTDI/CP210x, and adapters wired directly to a board's TX/RX pins.

Built on `System.IO.Ports`, which is supported on **Windows, Linux, macOS and Mac Catalyst**.

For a permanently installed device, prefer this over BLE: there is no pairing, no scan, no reconnect
storm after a power cycle, and a wired adapter cannot wander out of range.

### Platform support

| Platform | Supported | Notes |
|---|---|---|
| Windows, Linux, macOS | Yes | |
| Mac Catalyst | Yes | Marked `[SupportedOSPlatform]` on the assembly |
| **Android** | **No** | Builds and loads — but see below |
| iOS, tvOS, Browser/WASM | No | `[UnsupportedOSPlatform]`; throws `PlatformNotSupportedException` |

**Android deserves a specific warning.** `System.IO.Ports` is *not* marked unsupported there and its
native library genuinely ships for the `android-*` RIDs, so a `net10.0-android` project can reference
this package and compile cleanly. It then fails at runtime with `UnauthorizedAccessException` rather
than `PlatformNotSupportedException`, which looks like a fixable permissions problem and is not: the
kernel does create `/dev/ttyUSB0` for a host-mode adapter, but it is owned `root:usb` and an app's UID
is not in that group, so only a rooted device can open it.

Android's supported route for a USB adapter is the **USB Host API** (`UsbManager` → runtime
permission intent → `openDevice()` → bulk transfers), with the FTDI/CH340/CP210x/CDC-ACM protocol
implemented in user space — a different `IObdTransport`, not a variation on this one. On Android, use
`Shiny.Obd.Ble`.

### Registration

```csharp
services.AddShinyObdSerial(config =>
{
    config.PortName = "/dev/serial/by-id/usb-FTDI_FT232R_USB_UART_A50285BI-if00-port0";
    config.BaudRate = 115200;
});

// or, letting it discover the adapter
services.AddShinyObdSerial(config => config.PortNameFilter = "OBDLink");
```

### Direct construction

```csharp
var transport = new SerialObdTransport(new SerialObdConfiguration
{
    // Null discovers a port; see the by-id note below
    PortName = null,
    PortNameFilter = "OBDLink",

    // Probes 38400 / 115200 / 9600 / 500000 rather than trusting BaudRate
    AutoDetectBaudRate = true,

    CommandTimeout = TimeSpan.FromSeconds(10)
});

var connection = new ObdConnection(transport);
await connection.Connect();
```

`ConnectedPortName` and `ConnectedBaudRate` report what was actually opened, which is worth logging
when discovery is in play.

### Discovery

`SerialObdDeviceScanner` implements `IObdDeviceScanner`, so a "pick your adapter" UI works over
serial and BLE without caring which is which. `ObdDiscoveredDevice.NativeDevice` is a
`SerialPortInfo`.

```csharp
var scanner = new SerialObdDeviceScanner();
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

await scanner.Scan(device => Console.WriteLine(device.Name), cts.Token);
```

Discovery is platform-aware rather than a raw `SerialPort.GetPortNames()`:

| Platform | Enumerated | Notes |
|---|---|---|
| Linux | `/dev/serial/by-id/*`, then `ttyUSB*` / `ttyACM*` / `ttyAMA*` / `serial*` | `by-id` names come from the USB descriptor, so they carry the vendor and serial number **and survive a reboot** |
| macOS | `/dev/cu.*` | Only the `cu.` nodes — opening the matching `tty.` node blocks until the device asserts carrier detect, which a USB-serial bridge never does |
| Windows | `SerialPort.GetPortNames()` | Backed by the SERIALCOMM device map |

Ports likely to be adapters are returned first, matched against known brands (OBDLink, Veepeak,
Vgate, ScanTool) and the USB-serial bridge chips they are built on (FTDI, CH340, CP210x, PL2303).
The bridges are included deliberately: a genuine OBDLink SX presents as a stock FTDI device with no
OBD branding in its descriptor.

> **Prefer a `by-id` path over `/dev/ttyUSB0` on Linux.** The numbered names are assigned in USB
> enumeration order, so a second USB serial device — a GNSS puck, a cellular modem — can take
> `ttyUSB0` out from under your adapter across a reboot.

### Linux permissions

Opening a serial port needs the `dialout` group:

```bash
sudo usermod -aG dialout $USER   # then log out and back in
```

ModemManager will also probe any USB-serial device it sees and hold it open for several seconds
sending AT commands, which makes connects fail intermittently. Tell it to leave the bridges alone:

```
# /etc/udev/rules.d/77-no-modemmanager-obd.rules
ATTRS{idVendor}=="0403", ENV{ID_MM_DEVICE_IGNORE}="1"   # FTDI
ATTRS{idVendor}=="1a86", ENV{ID_MM_DEVICE_IGNORE}="1"   # CH340
ATTRS{idVendor}=="10c4", ENV{ID_MM_DEVICE_IGNORE}="1"   # CP210x
```

## Raw Commands

Send arbitrary AT or OBD commands:

```csharp
// AT commands
var version = await connection.SendRaw("ATI");      // "ELM327 v1.5"
var protocol = await connection.SendRaw("ATDPN");   // current protocol number
var voltage = await connection.SendRaw("ATRV");     // battery voltage

// Raw OBD hex commands
var response = await connection.SendRaw("0100");    // supported PIDs [01-20]
```

## Error Handling

`ObdException` is thrown for adapter-level errors:

```csharp
try
{
    var speed = await connection.Execute(StandardCommands.VehicleSpeed);
}
catch (ObdException ex) when (ex.Message.Contains("No data"))
{
    // Vehicle not responding to this PID (engine off, unsupported PID, etc.)
}
catch (ObdException ex) when (ex.Message.Contains("Unable to connect"))
{
    // Adapter can't reach the vehicle ECU
}
```

The `ObdCommand<T>` base class also validates response headers and throws `ObdException` on mode/PID mismatches.

### Timeouts

When an adapter stops answering, the transport throws `ObdTimeoutException` — an `ObdException`, deliberately **not** an `OperationCanceledException`:

```csharp
try
{
    var speed = await connection.Execute(StandardCommands.VehicleSpeed, ct);
}
catch (ObdTimeoutException ex)
{
    // ex.Command / ex.Timeout — the adapter went quiet, but we are still running
}
catch (OperationCanceledException)
{
    // our own token fired — we are shutting down
}
```

The distinction matters most to a polling loop. If a transport reported its own deadline as a cancellation, a single slow reply would be indistinguishable from a shutdown request and would tear the loop down. Catch the timeout, skip that reading, and keep going — and if timeouts keep coming, rebuild the connection rather than assuming the session is still good, because a BLE adapter stays linked long after it stops talking to the vehicle.

## Implementing a Custom Transport

BLE, WiFi and serial ship in the box. Implement `IObdTransport` for anything else — an Android USB
Host API transport, a J2534 pass-thru box, a replay harness over a recorded session:

```csharp
public class UsbHostObdTransport : IObdTransport
{
    public bool IsConnected { get; private set; }

    public async Task Connect(CancellationToken ct = default)
    {
        // Open the channel — UsbManager.OpenDevice, a socket, a serial port…
    }

    public Task Disconnect() { /* ... */ }

    public async Task<string> Send(string command, CancellationToken ct = default)
    {
        // Send command, collect response until '>' prompt, return the text
    }

    public ValueTask DisposeAsync() { /* ... */ }
}
```

The `Send` method must:
1. Write the command string to the adapter
2. Read the response until the `>` prompt character
3. Return the response text (without the `>` prompt)

## Adapter Emulator (Sample)

Testing an OBD app usually means sitting in a car with the engine running. The sample app in
[`samples/Sample.Maui`](samples/Sample.Maui) can also *be* the adapter, so you can do it at a desk.

It runs both roles at once — the **Scan** tab is a client that finds and reads a real adapter, and the
**Adapter**, **Drive**, **Values** and **Faults** tabs turn the device into an ELM327-compatible OBD-II
adapter that other apps connect to. Point one device at another, or point any third-party OBD app at it.

```bash
dotnet build samples/Sample.Maui/Sample.Maui.csproj -f net10.0-android   # or -f net10.0-ios
```

Hosting starts the moment the app launches — there is no button to press first — and the Adapter tab
shows what is being advertised, who is connected, and every command as it arrives.

**Two transports, one vehicle.** A GATT service on `FFF0`/`FFF1`/`FFF2` advertised as `VEEPEAK` (the
`BleObdConfiguration` defaults, so the Scan tab on a second device finds it with no configuration),
and a TCP server on port 35000 (the first endpoint `WifiObdConfiguration` probes). A value you change
is answered identically over both.

**Discoverable over the network.** Real WiFi adapters do not announce themselves, which is why
`WifiObdTransport` walks a list of well-known addresses. The sample publishes itself over mDNS with
[Shiny.Net.Discovery](https://www.nuget.org/packages/Shiny.Net.Discovery), so a client can browse
`_obd._tcp` and get the endpoint back directly:

```csharp
await foreach (var result in mdns.Browse("_obd._tcp", ct))
{
    if (result.Status != MdnsBrowseStatus.Found)
        continue;

    var endpoint = result.Service.GetEndPoint();
    var connection = new ObdConnection(
        new WifiObdTransport(endpoint!.Address.ToString(), endpoint.Port)
    );
    await connection.Connect();
}
```

**It behaves like the real thing.** The whole `Elm327AdapterProfile` init sequence (`ATZ`, `ATE`,
`ATL`, `ATS`, `ATH`, `ATSP`) plus `ATI`, `AT@1`, `ATRV`, `ATDP` and `ATDPN`; echo on at power-up, as a
real adapter does; responses chunked into 20-byte BLE notifications; and multi-frame replies carrying
the byte-count line and numbered frames, so a VIN read exercises the real path rather than pretending
it away. Modes 01, 02, 03, 04, 06, 07, 09 and 0A are all answered.

**Every command is settable.** The Values tab lists all 90-odd commands, searchable by name or request
(`010C`, `0902`). Each has a supported switch — turn a PID off and it answers `NO DATA` *and* drops out
of the supported-PID bitmask, so a client walking `0100`/`0120`/`0140` discovers exactly the set you
left on — an editor in engineering units, a raw hex override for composite PIDs or deliberately
malformed replies, and a readback showing the bytes going on the wire next to what a `Shiny.Obd` client
decodes from them. That readback runs the emulator's own bytes back through the real command object,
so an encoder that disagrees with the library's parser shows up there rather than as a wrong number
somewhere else.

**Or let it drive itself.** Hand-set values are a flat line, which will not catch a client that
mishandles a gear change or an hour of continuous polling. The Drive tab plays a looping scenario into
the emulator at five updates a second — **Warm idle**, **City driving** (lights, a school zone, a
roundabout, one emergency stop), **Busy highway** (merge, overtakes, a truck cutting in, a stop-and-go
jam) or **Mixed commute** (city, highway, city — about half an hour a lap). One model of a car drives
every parameter, so RPM matches the gear the speed implies, mass air flow matches the load, fuel rate
matches the air flow, and the odometer, fuel level and trip counters integrate across laps. Gear
changes, deceleration fuel cut and harsh braking are all in there. Everything the scenario does not
model — the supported switches, the fault memory, the adapter identity — stays where you set it, so you
can add a trouble code mid-drive.

**Faults and identity too.** MIL and stored-code count (which drive PIDs 01 and 41), readiness monitors
complete or still running, compression vs spark ignition, freeze frame stored or not, ignition off
(every request answers `UNABLE TO CONNECT`), trouble codes for modes 03/07/0A, and the `ATI` response —
put `STN` in it and `ObdConnection` picks the OBDLink profile instead of the ELM327 one, which is how
you test that branch without owning an OBDLink.
