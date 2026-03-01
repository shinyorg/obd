namespace Shiny.Obd;

/// <summary>
/// Represents a discovered OBD adapter device found during scanning.
/// </summary>
public class ObdDiscoveredDevice
{
    public ObdDiscoveredDevice(string name, string id, object nativeDevice)
    {
        this.Name = name;
        this.Id = id;
        this.NativeDevice = nativeDevice;
    }

    /// <summary>
    /// Display name of the device (e.g. "OBDLink MX+", "OBDII")
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Unique identifier for the device (e.g. BLE peripheral UUID, IP address)
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Transport-specific native device object.
    /// For BLE this is <c>IPeripheral</c>, for WiFi it could be <c>IPEndPoint</c>, etc.
    /// </summary>
    public object NativeDevice { get; }
}
