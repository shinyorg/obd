using System;

namespace Shiny.Obd.Ble;

/// <summary>
/// Configuration for the BLE OBD transport. UUIDs vary by adapter manufacturer.
/// Defaults are set for common ELM327 BLE clones (FFF0/FFF1/FFF2).
/// </summary>
public class BleObdConfiguration
{
    /// <summary>
    /// GATT service UUID of the OBD adapter
    /// </summary>
    public string ServiceUuid { get; set; } = "FFF0";

    /// <summary>
    /// Characteristic UUID to subscribe for read notifications (RX from adapter)
    /// </summary>
    public string ReadCharacteristicUuid { get; set; } = "FFF1";

    /// <summary>
    /// Characteristic UUID to write commands to (TX to adapter)
    /// </summary>
    public string WriteCharacteristicUuid { get; set; } = "FFF2";

    /// <summary>
    /// Optional device name filter for BLE scanning. Null matches any device with the service UUID.
    /// </summary>
    public string? DeviceNameFilter { get; set; }

    /// <summary>
    /// Timeout for a single OBD command response
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Whether to hand the platform a standing reconnect for the adapter. Off by default.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is not "reconnect faster" — it is the opposite. On Android it selects
    /// <c>ConnectGatt(autoConnect: true)</c>, the background connection path, where the controller only
    /// attempts during widely spaced scan windows: an in-range adapter that a direct connect reaches in
    /// a few hundred milliseconds takes tens of seconds this way. It is the right setting for a
    /// peripheral you want re-established whenever it happens to reappear, and the wrong one for an
    /// adapter a caller is actively waiting on.
    /// <para>
    /// It also arms the platform's own reconnect, which will race a caller that supervises the session
    /// itself — each side's teardown cancels the other's attempt in flight.
    /// </para>
    /// </remarks>
    public bool AutoConnect { get; set; }

    /// <summary>
    /// How long to wait for the BLE link itself, before any OBD initialization.
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
