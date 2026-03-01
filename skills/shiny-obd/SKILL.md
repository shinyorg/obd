---
name: shiny-obd
description: Generate code using Shiny.Obd, an OBD-II vehicle communication library for .NET with command-object pattern, adapter auto-detection, and BLE transport
auto_invoke: true
triggers:
  - obd
  - obd-ii
  - obd2
  - vehicle diagnostics
  - elm327
  - obdlink
  - IObdCommand
  - IObdConnection
  - IObdTransport
  - ObdCommand
  - ObdConnection
  - StandardCommands
  - vehicle speed
  - engine rpm
  - coolant temperature
  - throttle position
  - vin command
  - obd ble
  - BleObdTransport
  - obd adapter
  - adapter profile
  - Shiny.Obd
---

# Shiny.Obd Skill

You are an expert in Shiny.Obd, a .NET library for communicating with vehicles through OBD-II adapters. It uses a command-object pattern with generic return types, pluggable transports (BLE first), and adapter auto-detection for ELM327 and OBDLink (STN) adapters.

## When to Use This Skill

Invoke this skill when the user wants to:
- Read vehicle data (speed, RPM, coolant temp, VIN, etc.) through OBD-II
- Create custom OBD commands with typed return values
- Connect to an OBD-II adapter over Bluetooth LE
- Configure ELM327 or OBDLink adapter initialization
- Implement a custom transport (WiFi, USB) for OBD communication
- Send raw AT commands to an OBD adapter
- Handle OBD response parsing and error handling

## Library Overview

- **Repository**: https://github.com/shinyorg/obd
- **Namespaces**: `Shiny.Obd`, `Shiny.Obd.Ble`, `Shiny.Obd.Commands`
- **NuGet**: `Shiny.Obd` (core), `Shiny.Obd.Ble` (BLE transport)
- **Targets**: `netstandard2.1` (core), `net10.0` (BLE)

## Core Types

### IObdCommand<T> — Command interface

Every OBD command implements this. `T` is the parsed result type.

```csharp
public interface IObdCommand<T>
{
    string RawCommand { get; }
    T Parse(byte[] data);
}
```

### ObdCommand<T> — Base class for standard Mode/PID commands

Validates mode+PID response header, strips it, and delegates to `ParseData`.

```csharp
public abstract class ObdCommand<T> : IObdCommand<T>
{
    protected ObdCommand(byte mode, byte pid);
    public byte Mode { get; }
    public byte Pid { get; }
    public virtual string RawCommand { get; } // "{Mode:X2}{Pid:X2}"
    protected abstract T ParseData(byte[] data); // data after header
}
```

### IObdConnection — Connection interface

```csharp
public interface IObdConnection : IAsyncDisposable
{
    bool IsConnected { get; }
    Task Connect(CancellationToken ct = default);
    Task Disconnect();
    Task<T> Execute<T>(IObdCommand<T> command, CancellationToken ct = default);
    Task<string> SendRaw(string command, CancellationToken ct = default);
}
```

### IObdTransport — Transport abstraction

```csharp
public interface IObdTransport : IAsyncDisposable
{
    bool IsConnected { get; }
    Task Connect(CancellationToken ct = default);
    Task Disconnect();
    Task<string> Send(string command, CancellationToken ct = default);
}
```

### ObdConnection — ELM327 protocol handler

Two constructors:
- `ObdConnection(IObdTransport transport)` — auto-detects adapter via ATI
- `ObdConnection(IObdTransport transport, IObdAdapterProfile profile)` — uses explicit profile, skips detection

Properties:
- `DetectedAdapter` — `ObdAdapterInfo?` with `RawIdentifier` (string) and `Type` (ObdAdapterType enum: Unknown, Elm327, ObdLink). Null when explicit profile used.

Handles:
- ELM327 hex response parsing (single-line and multi-frame CAN)
- Error detection: "NO DATA", "UNABLE TO CONNECT", "BUS INIT: ...ERROR", "?"
- Strips "SEARCHING..." and "BUS INIT" prefixes

### IObdAdapterProfile — Adapter initialization

```csharp
public interface IObdAdapterProfile
{
    string Name { get; }
    Task Initialize(IObdConnection connection, CancellationToken ct = default);
}
```

Built-in profiles:
- `Elm327AdapterProfile` — ATZ, ATE0, ATL0, ATS1, ATH0, ATSP0
- `ObdLinkAdapterProfile` — extends Elm327 with STFAC, ATCAF1

## Standard Commands (StandardCommands static class)

| Property | Type | Command | Return | Parse Formula |
|----------|------|---------|--------|---------------|
| `VehicleSpeed` | `VehicleSpeedCommand` | `010D` | `int` (km/h) | `A` |
| `EngineRpm` | `EngineRpmCommand` | `010C` | `int` (RPM) | `((A*256)+B)/4` |
| `CoolantTemperature` | `CoolantTemperatureCommand` | `0105` | `int` (°C) | `A-40` |
| `ThrottlePosition` | `ThrottlePositionCommand` | `0111` | `double` (%) | `(A*100)/255` |
| `FuelLevel` | `FuelLevelCommand` | `012F` | `double` (%) | `(A*100)/255` |
| `CalculatedEngineLoad` | `CalculatedEngineLoadCommand` | `0104` | `double` (%) | `(A*100)/255` |
| `IntakeAirTemperature` | `IntakeAirTemperatureCommand` | `010F` | `int` (°C) | `A-40` |
| `RuntimeSinceStart` | `RuntimeSinceStartCommand` | `011F` | `TimeSpan` | `(A*256)+B` seconds |
| `Vin` | `VinCommand` | `0902` | `string` | skip count byte, ASCII decode |

## BLE Transport (Shiny.Obd.Ble)

### BleObdConfiguration

```csharp
public class BleObdConfiguration
{
    public string ServiceUuid { get; set; } = "FFF0";
    public string ReadCharacteristicUuid { get; set; } = "FFF1";
    public string WriteCharacteristicUuid { get; set; } = "FFF2";
    public string? DeviceNameFilter { get; set; }
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
```

### BleObdTransport

Two constructors:
- `BleObdTransport(IBleManager bleManager, BleObdConfiguration config)` — scans for adapter
- `BleObdTransport(IPeripheral peripheral, BleObdConfiguration config)` — uses pre-discovered peripheral

Uses Shiny.BluetoothLE v4 APIs:
- `ConnectAsync` for task-based connection
- `NotifyCharacteristic` for RX notifications
- `WriteCharacteristicAsync` for TX writes
- Collects notification bytes until `>` prompt, returns complete response

## Code Generation Patterns

### Creating a custom OBD command (standard PID)

```csharp
public class BarometricPressureCommand : ObdCommand<int>
{
    public BarometricPressureCommand() : base(0x01, 0x33) { }
    protected override int ParseData(byte[] data) => data[0];
}
```

### Creating a custom OBD command (non-standard)

```csharp
public class ManufacturerCommand : IObdCommand<string>
{
    public string RawCommand => "2101";
    public string Parse(byte[] data) => BitConverter.ToString(data);
}
```

### Full connection setup with BLE

```csharp
var transport = new BleObdTransport(bleManager, new BleObdConfiguration
{
    DeviceNameFilter = "OBDLink"
});
var connection = new ObdConnection(transport);
await connection.Connect();

var speed = await connection.Execute(StandardCommands.VehicleSpeed);
var rpm = await connection.Execute(StandardCommands.EngineRpm);
var vin = await connection.Execute(StandardCommands.Vin);
```

### Explicit adapter profile

```csharp
var connection = new ObdConnection(transport, new ObdLinkAdapterProfile());
await connection.Connect();
```

### Custom adapter profile

```csharp
public class MyAdapterProfile : IObdAdapterProfile
{
    public string Name => "MyAdapter";
    public async Task Initialize(IObdConnection connection, CancellationToken ct = default)
    {
        await connection.SendRaw("ATZ", ct);
        await Task.Delay(500, ct);
        await connection.SendRaw("ATE0", ct);
        await connection.SendRaw("ATSP6", ct); // force CAN 11-bit 500kbaud
    }
}
```

### Custom transport implementation

```csharp
public class WifiObdTransport : IObdTransport
{
    public bool IsConnected { get; private set; }
    public async Task Connect(CancellationToken ct = default) { /* TCP connect */ }
    public Task Disconnect() { /* close socket */ }
    public async Task<string> Send(string command, CancellationToken ct = default)
    {
        // Write command, read until '>' prompt, return response without '>'
    }
    public ValueTask DisposeAsync() { /* cleanup */ }
}
```

## Important Notes

- All async methods do NOT use the `Async` suffix (e.g. `Connect`, `Execute`, `SendRaw`).
- `ObdCommand<T>.ParseData` receives bytes AFTER the 2-byte mode+PID header is stripped.
- `IObdCommand<T>.Parse` receives ALL response bytes including mode+PID header.
- `ObdConnection` appends `\r` to all commands sent via `SendRaw` before passing to transport.
- BLE transport uses `SemaphoreSlim` to serialize commands (one at a time).
- ELM327 response parser handles both single-line (`"41 0D 50"`) and multi-frame CAN (`"0: 49 02 01 57 42\r1: 41 30..."`) formats.
