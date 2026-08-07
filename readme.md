# Shiny.Obd

[![NuGet](https://img.shields.io/nuget/v/Shiny.Obd.svg?style=flat-square)](https://www.nuget.org/packages/Shiny.Obd/)

A .NET library for communicating with vehicles through OBD-II (On-Board Diagnostics) adapters. Supports ELM327 and OBDLink (STN) adapters over pluggable transports, starting with Bluetooth LE.

[Documentation](https://shinylib.net/client/obd)

## Features

- **Command-object pattern** — OBD commands are objects, not methods. Pass built-in commands or create your own for custom PIDs.
- **Generic return types** — each command declares its return type (`int`, `double`, `string`, `TimeSpan`, etc.) with compile-time safety.
- **Pluggable transports** — `IObdTransport` abstracts the communication channel. Ship with BLE; add WiFi or USB later.
- **Adapter auto-detection** — detects ELM327 vs OBDLink (STN) adapters via ATI and runs the appropriate initialization sequence.
- **Adapter profiles** — `IObdAdapterProfile` lets you define custom init sequences. Built-in profiles for ELM327 and OBDLink.
- **Multi-frame CAN responses** — the byte-count line and per-frame `N:` index an ELM327 prints for a large reply (the VIN, or mode 03 with three or more codes) are treated as framing and discarded, with spaced and unspaced hex both accepted.
- **Task-based async** — fully async/await throughout, no Reactive Extensions required in consuming code.
- **30+ standard commands included** — speed, RPM, temperatures, pressures, throttle and pedal position, fuel level, trims, rate and system status, timing advance, engine load, odometer, distances and timers, VIN, calibration IDs, hybrid battery life and more.
- **Supported-PID discovery** — `SupportedPidsCommand` reads the mode 01 bitmask blocks so you only ever query readings the vehicle in front of you actually reports.
- **Diagnostic trouble codes** — read stored, pending and permanent codes (modes 03/07/0A) as SAE J2012 strings, and clear them (mode 04). CAN and pre-CAN response framing are both handled.
- **Emissions monitor readiness** — the full mode 01 PID 01/41 bit layout decoded for both spark and compression ignition, with `IsReadyForInspection` answering the question an emissions test actually asks.
- **Freeze frames** — `AsFreezeFrame()` on any mode 01 command reads the same PID out of the snapshot the ECU stored when a code was set, so you get the conditions at the moment of the fault rather than the conditions now.
- **VIN decoding** — `IVinDecoder` turns the VIN off the ECU into make, model, year and the engine/drivetrain/body a registry knows about. NHTSA vPIC ships built in (free, keyless); register your own provider with one line.

## Projects

| Package | Target | Description |
|---------|--------|-------------|
| `Shiny.Obd` | `net10.0` | Core library — commands, connection, transport abstraction |
| `Shiny.Obd.Ble` | `net10.0` | BLE transport using [Shiny.BluetoothLE](https://github.com/shinyorg/shiny) |

## Quick Start

### 1. Install packages

```xml
<!-- Core (always needed) -->
<PackageReference Include="Shiny.Obd" />

<!-- BLE transport -->
<PackageReference Include="Shiny.Obd.Ble" />
```

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
│  ┌──────────────┐  ┌────────┐  ┌─────────┐     │
│  │ BleObdTransport│  │ WiFi  │  │  USB    │     │
│  │ (Shiny BLE)  │  │(future)│  │(future) │     │
│  └──────────────┘  └────────┘  └─────────┘     │
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

Implement `IObdTransport` to add WiFi, USB, or any other communication channel:

```csharp
public class WifiObdTransport : IObdTransport
{
    public bool IsConnected { get; private set; }

    public async Task Connect(CancellationToken ct = default)
    {
        // Connect to ELM327 WiFi adapter (typically 192.168.0.10:35000)
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
