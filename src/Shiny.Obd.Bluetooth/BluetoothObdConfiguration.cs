using System;
using Shiny.Bluetooth;

namespace Shiny.Obd.Bluetooth;

/// <summary>
/// Configuration for the classic Bluetooth (RFCOMM/SPP) OBD transport.
/// </summary>
/// <remarks>
/// The defaults suit the common classic adapters - the ELM327 v1.5 "Bluetooth" clones, OBDLink LX/MX+,
/// Vgate iCar and Veepeak BT. Nothing normally needs setting: leaving <see cref="Address"/> null picks
/// the first paired device whose name matches <see cref="DeviceNameFilters"/>.
/// </remarks>
public class BluetoothObdConfiguration
{
    /// <summary>
    /// The adapter's Bluetooth address (<c>AA:BB:CC:DD:EE:FF</c>). Null means "pick one" from the
    /// paired devices - see <see cref="DeviceNameFilters"/>.
    /// </summary>
    /// <remarks>
    /// The address is the only identifier a classic adapter carries that survives a rename or a second
    /// adapter of the same model being paired, so persist it once a user has picked one.
    /// </remarks>
    public string? Address { get; set; }

    /// <summary>
    /// Name fragments (case-insensitive) that mark a device as a likely OBD adapter, used when
    /// <see cref="Address"/> is null and by <see cref="BluetoothObdDeviceScanner"/>. Empty matches every
    /// device, including unnamed ones.
    /// </summary>
    /// <remarks>
    /// Classic Bluetooth has no service UUID to filter on before connecting - an SDP lookup costs a
    /// baseband connection per device - so the name is the only cheap signal. The defaults cover the
    /// names the common adapters ship with ("OBDII", "OBD2", "OBDLink MX+", "V-LINK", "Vgate iCar",
    /// "VEEPEAK", "KONNWEI").
    /// </remarks>
    public string[] DeviceNameFilters { get; set; } = ["OBD", "ELM", "V-LINK", "VLINK", "VGATE", "VEEPEAK", "KONNWEI", "ICAR"];

    /// <summary>
    /// The RFCOMM service to connect to. Serial Port Profile (0x1101), which every classic OBD adapter
    /// publishes.
    /// </summary>
    public Guid ServiceUuid { get; set; } = BluetoothServiceUuids.SerialPort;

    /// <summary>
    /// Require an authenticated, encrypted link - which pairs on the way in when the adapter is not
    /// already paired. On by default.
    /// </summary>
    /// <remarks>
    /// Turn this off only for a clone that refuses to pair at all. Android then opens an "insecure"
    /// socket and Windows a <c>PlainSocket</c>; macOS always lets the system decide.
    /// </remarks>
    public bool Secure { get; set; } = true;

    /// <summary>
    /// How long a single RFCOMM connection attempt may take, SDP lookup included.
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How many times to attempt the RFCOMM connection before giving up.
    /// </summary>
    /// <remarks>
    /// The first connect after an adapter powers up, or straight after an inquiry, fails often enough
    /// on Android ("read failed, socket might closed") that a single retry is worth having by default.
    /// </remarks>
    public int ConnectAttempts { get; set; } = 2;

    /// <summary>
    /// Pause between connection attempts.
    /// </summary>
    public TimeSpan ConnectRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Timeout for a single OBD command response.
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Whether <see cref="BluetoothObdDeviceScanner"/> runs an inquiry for unpaired adapters after
    /// listing the paired ones. On by default.
    /// </summary>
    /// <remarks>
    /// An inquiry takes about 12 seconds and slows any connection on the same radio while it runs,
    /// which is why the scanner reports paired devices first and the transport never runs one itself.
    /// </remarks>
    public bool DiscoverUnpaired { get; set; } = true;


    /// <summary>
    /// Whether <paramref name="name"/> matches <see cref="DeviceNameFilters"/>.
    /// </summary>
    public bool Matches(string? name)
    {
        if (this.DeviceNameFilters is not { Length: > 0 })
            return true;

        if (String.IsNullOrWhiteSpace(name))
            return false;

        foreach (var filter in this.DeviceNameFilters)
        {
            if (name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
