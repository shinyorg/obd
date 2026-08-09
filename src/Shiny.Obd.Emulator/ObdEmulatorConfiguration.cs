namespace Shiny.Obd.Emulator;

/// <summary>
/// How the emulator presents itself on each transport.
/// </summary>
/// <remarks>
/// The BLE defaults are the FFF0/FFF1/FFF2 triple used by Veepeak OBDCheck BLE and the ELM327 clones
/// built on the same module - and they are what <c>BleObdConfiguration</c> defaults to, so a
/// Shiny.Obd client scanning for an adapter finds this emulator with no configuration on either side.
/// </remarks>
public class ObdEmulatorConfiguration
{
    /// <summary>GATT service the emulator advertises.</summary>
    public string ServiceUuid { get; set; } = "FFF0";

    /// <summary>Characteristic the emulator notifies responses on (adapter to app).</summary>
    public string NotifyCharacteristicUuid { get; set; } = "FFF1";

    /// <summary>Characteristic the emulator accepts commands on (app to adapter).</summary>
    public string WriteCharacteristicUuid { get; set; } = "FFF2";

    /// <summary>Advertised local name. Veepeak units call themselves this.</summary>
    public string LocalName { get; set; } = "VEEPEAK";

    /// <summary>TCP port for the WiFi-adapter equivalent. 35000 is what WifiObdConfiguration probes first.</summary>
    public int TcpPort { get; set; } = 35000;

    /// <summary>DNS-SD service type published over mDNS.</summary>
    public string MdnsServiceType { get; set; } = "_obd._tcp";

    /// <summary>DNS-SD instance name. Must be unique on the link.</summary>
    public string MdnsInstanceName { get; set; } = "Shiny OBD Emulator";
}
