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
}
