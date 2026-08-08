namespace Shiny.Obd.Wifi;

/// <summary>
/// A host/port pair an ELM327 WiFi adapter may be listening on.
/// </summary>
/// <param name="Host">Hostname or IP literal. These adapters are their own access point, so this is
/// almost always a private IPv4 address rather than a name.</param>
/// <param name="Port">TCP port. 35000 is the de-facto standard; a handful of clones use 23.</param>
public record WifiObdEndpoint(string Host, int Port)
{
    /// <summary>Renders as <c>host:port</c>.</summary>
    public override string ToString() => $"{this.Host}:{this.Port}";
}
