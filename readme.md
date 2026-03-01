# Shiny.Obd

A .NET library for communicating with vehicles through OBD-II (On-Board Diagnostics) adapters. Supports ELM327 and OBDLink (STN) adapters over pluggable transports, starting with Bluetooth LE.

## Features

- **Command-object pattern** — OBD commands are objects, not methods. Pass built-in commands or create your own for custom PIDs.
- **Generic return types** — each command declares its return type (`int`, `double`, `string`, `TimeSpan`, etc.) with compile-time safety.
- **Pluggable transports** — `IObdTransport` abstracts the communication channel. Ship with BLE; add WiFi or USB later.
- **Adapter auto-detection** — detects ELM327 vs OBDLink (STN) adapters via ATI and runs the appropriate initialization sequence.
- **Adapter profiles** — `IObdAdapterProfile` lets you define custom init sequences. Built-in profiles for ELM327 and OBDLink.
- **Task-based async** — fully async/await throughout, no Reactive Extensions required in consuming code.
- **9 standard commands included** — speed, RPM, coolant temp, throttle, fuel level, engine load, intake air temp, runtime, and VIN.

## Projects

| Package | Target | Description |
|---------|--------|-------------|
| `Shiny.Obd` | `netstandard2.1` | Core library — commands, connection, transport abstraction |
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
