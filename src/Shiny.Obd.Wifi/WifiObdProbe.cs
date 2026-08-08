using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Shiny.Obd.Wifi;

/// <summary>
/// Endpoint list building and the one-shot "is there an ELM327 on this socket" check, shared by
/// <see cref="WifiObdTransport"/> and <see cref="WifiObdDeviceScanner"/>.
/// </summary>
static class WifiObdProbe
{
    /// <summary>
    /// The ordered list of endpoints to try for a given configuration.
    /// </summary>
    public static IReadOnlyList<WifiObdEndpoint> BuildCandidates(WifiObdConfiguration config)
    {
        var list = new List<WifiObdEndpoint>();

        // A configured host always goes first. When auto-detection is also on this costs nothing in
        // the common case - the first candidate answers and the rest are never tried.
        if (!String.IsNullOrWhiteSpace(config.Host))
            list.Add(new WifiObdEndpoint(config.Host, config.Port));

        if (config.Host == null || config.AutoDetectEndpoint)
        {
            if (config.IncludeGatewayCandidates)
            {
                foreach (var gateway in DefaultGateways())
                    list.Add(new WifiObdEndpoint(gateway, config.Port));
            }

            list.AddRange(config.EndpointCandidates);
        }

        // Records compare by value, so the gateway heuristic naturally collapses into the well-known
        // list when it turns up 192.168.0.10 rather than probing it twice.
        return list.Distinct().ToList();
    }

    /// <summary>
    /// The IPv4 default gateway of every up, non-loopback interface.
    /// </summary>
    /// <remarks>
    /// A WiFi OBD adapter runs the access point you joined, so it is the gateway of that link. This
    /// is best-effort: some platforms report no gateway for a network with no internet route, which
    /// is exactly what these adapters are, hence the well-known list as well.
    /// </remarks>
    public static IReadOnlyList<string> DefaultGateways()
    {
        var found = new List<string>();

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;

                foreach (var gateway in nic.GetIPProperties().GatewayAddresses)
                {
                    var address = gateway.Address;
                    if (address?.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    // A 0.0.0.0 gateway is how some platforms spell "none".
                    if (address.Equals(IPAddress.Any))
                        continue;

                    found.Add(address.ToString());
                }
            }
        }
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException)
        {
            // Enumerating interfaces is a nice-to-have, never the reason a connect fails.
            return [];
        }

        return found;
    }

    /// <summary>
    /// Opens a throwaway connection and returns the adapter's ATI identity, or null if nothing
    /// ELM327-shaped is there.
    /// </summary>
    /// <remarks>
    /// The socket is fully closed before this returns, which matters more than it looks: most of
    /// these adapters accept exactly one TCP client, so a probe that lingered would lock out the
    /// transport that is about to connect for real.
    ///
    /// <para>
    /// The prompt does the discriminating. A reply only counts when it is terminated by '&gt;', and
    /// nothing else on a home network ends a response that way - a router's telnet or HTTP port
    /// accepts the connection and then either says nothing to "ATI\r" or answers without a prompt,
    /// and both fall out as a timeout here rather than as a false positive.
    /// </para>
    /// </remarks>
    public static async Task<string?> Identify(
        WifiObdEndpoint endpoint,
        WifiObdConfiguration config,
        CancellationToken ct
    )
    {
        Socket? socket = null;
        try
        {
            socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = config.NoDelay };
            config.ConfigureSocket?.Invoke(socket);

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(config.ConnectTimeout);
            await socket.ConnectAsync(endpoint.Host, endpoint.Port, connectCts.Token).ConfigureAwait(false);

            using var stream = new NetworkStream(socket, ownsSocket: false);

            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCts.CancelAfter(config.ProbeTimeout);

            await stream.WriteAsync(Encoding.ASCII.GetBytes("ATI\r"), readCts.Token).ConfigureAwait(false);
            await stream.FlushAsync(readCts.Token).ConfigureAwait(false);

            var buffer = new byte[256];
            var text = new StringBuilder();

            while (true)
            {
                var read = await stream.ReadAsync(buffer, readCts.Token).ConfigureAwait(false);
                if (read <= 0)
                    return null;

                text.Append(Encoding.ASCII.GetString(buffer, 0, read));

                var current = text.ToString();
                if (current.Contains('>'))
                    return current.Replace(">", "").Trim();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or IOException or ArgumentException or ObjectDisposedException)
        {
            return null;
        }
        finally
        {
            socket?.Dispose();
        }
    }
}
