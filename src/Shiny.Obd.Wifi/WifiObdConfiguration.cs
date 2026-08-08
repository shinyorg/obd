using System;
using System.Net.Sockets;

namespace Shiny.Obd.Wifi;

/// <summary>
/// Configuration for the WiFi (TCP) OBD transport.
/// </summary>
/// <remarks>
/// The defaults suit the common WiFi adapters - OBDLink MX Wi-Fi, Veepeak WiFi, Vgate iCar, and the
/// generic ELM327 clones built on an ESP8266/ESP32. Nothing normally needs setting: leaving
/// <see cref="Host"/> null probes the well-known endpoints plus the current network's gateway, which
/// covers every adapter the author has seen.
/// </remarks>
public class WifiObdConfiguration
{
    /// <summary>
    /// The adapter's address. Null means "discover one" - see <see cref="AutoDetectEndpoint"/>.
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// The adapter's TCP port. 35000 is what nearly every ELM327 WiFi adapter listens on.
    /// </summary>
    public int Port { get; set; } = 35000;

    /// <summary>
    /// Endpoints to try, in order, when <see cref="AutoDetectEndpoint"/> is on.
    /// </summary>
    /// <remarks>
    /// <c>192.168.0.10:35000</c> is the OBDLink/ScanTool default and the one most clones copied;
    /// <c>192.168.4.1</c> is the stock SoftAP address of the ESP8266/ESP32 the cheaper ones are built
    /// on. Port 23 appears on a minority of clones that reused a telnet bridge firmware.
    /// </remarks>
    public WifiObdEndpoint[] EndpointCandidates { get; set; } =
    [
        new("192.168.0.10", 35000),
        new("192.168.4.1", 35000),
        new("192.168.1.5", 35000),
        new("10.0.0.10", 35000),
        new("192.168.0.10", 23),
        new("192.168.4.1", 23)
    ];

    /// <summary>
    /// Probe <see cref="EndpointCandidates"/> at connect time rather than trusting <see cref="Host"/>.
    /// When <see cref="Host"/> is also set it is simply tried first.
    /// </summary>
    /// <remarks>
    /// This is on by default because a TCP connect is not evidence of anything on its own - see the
    /// remarks on <see cref="WifiObdTransport"/>. Turning it off connects to <see cref="Host"/> and
    /// asks it no questions, which is the right choice only when you know exactly what is there.
    /// </remarks>
    public bool AutoDetectEndpoint { get; set; } = true;

    /// <summary>
    /// Also try the default gateway of every up, non-loopback interface.
    /// </summary>
    /// <remarks>
    /// These adapters run the access point you joined, so they *are* the gateway. This one heuristic
    /// covers adapters whose address is on none of the well-known lists, at the cost of one probe
    /// against your home router if you happen to run this while not joined to an adapter.
    /// </remarks>
    public bool IncludeGatewayCandidates { get; set; } = true;

    /// <summary>
    /// How long to wait for the TCP connect to a single candidate.
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a candidate gets to answer the ATI probe before it is written off.
    /// </summary>
    public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Timeout for a single OBD command response.
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Send a cheap ATI when the link has been idle this long, to stop the adapter dropping it.
    /// <see cref="TimeSpan.Zero"/> disables it.
    /// </summary>
    /// <remarks>
    /// Clone firmware commonly closes an idle socket after 30-60 seconds, and does it without a FIN
    /// often enough that the first thing you learn about it is a command timing out. A poll loop
    /// never goes idle and never notices; an app that connects and then waits for the user does.
    /// </remarks>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Disable Nagle's algorithm. Leave this on.
    /// </summary>
    /// <remarks>
    /// An OBD exchange is a tiny request followed by a tiny reply, which is precisely the traffic
    /// Nagle delays - it holds a small write back waiting for more data to coalesce with. With it
    /// enabled you pay tens of milliseconds on every single PID read, which is the difference between
    /// a usable live-data gauge and a sluggish one.
    /// </remarks>
    public bool NoDelay { get; set; } = true;

    /// <summary>
    /// Called with the raw socket after it is created and before it connects.
    /// </summary>
    /// <remarks>
    /// The escape hatch for platform socket work this library cannot do from a <c>net10.0</c>
    /// assembly - binding the socket to a specific <c>Android.Net.Network</c>, setting buffer sizes,
    /// enabling TCP keep-alives. See the Android routing note on <see cref="WifiObdTransport"/>.
    /// </remarks>
    public Action<Socket>? ConfigureSocket { get; set; }
}
