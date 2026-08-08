using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Shiny.Net.Discovery;

namespace Sample.Maui.Emulator;

/// <summary>
/// The WiFi-adapter equivalent of the BLE peripheral: a plain TCP server speaking the same ELM327
/// dialect, published on the local link over mDNS so clients can find it without knowing its address.
/// </summary>
/// <remarks>
/// WiFi OBD adapters are all TCP sockets that stream ELM327 text - port 35000 is the one
/// <c>WifiObdConfiguration</c> probes first. What they do <i>not</i> do is announce themselves, which
/// is why <c>WifiObdTransport</c> has to walk a list of well-known addresses. Publishing over mDNS is
/// the part this emulator adds: browse <c>_obd._tcp</c> and the endpoint comes back with its port.
/// </remarks>
public sealed class TcpObdServer(
    Elm327Responder responder,
    ObdHostConfiguration config,
    IMdnsManager mdns
)
{
    readonly List<Task> clientTasks = [];

    TcpListener? listener;
    CancellationTokenSource? cts;
    IMdnsPublication? publication;

    public bool IsRunning { get; private set; }

    public string Status { get; private set; } = "Not started";

    public string MdnsStatus { get; private set; } = "Not published";

    /// <summary>The addresses a client can point <c>WifiObdTransport</c> at.</summary>
    public IReadOnlyList<string> Endpoints { get; private set; } = [];

    public async Task<bool> Start(IObdHostSink sink)
    {
        if (this.IsRunning)
            return true;

        try
        {
            this.listener = new TcpListener(IPAddress.Any, config.TcpPort);
            this.listener.Start();
        }
        catch (Exception ex)
        {
            this.Status = $"Could not listen on port {config.TcpPort}: {ex.Message}";
            return false;
        }

        this.cts = new CancellationTokenSource();
        this.Endpoints = [.. LocalAddresses().Select(x => $"{x}:{config.TcpPort}")];
        this.IsRunning = true;
        this.Status = $"Listening on port {config.TcpPort}";

        _ = this.AcceptLoop(sink, this.cts.Token);
        await this.Publish(sink).ConfigureAwait(true);

        return true;
    }

    public async Task Stop()
    {
        this.cts?.Cancel();
        this.cts?.Dispose();
        this.cts = null;

        this.listener?.Stop();
        this.listener = null;

        if (this.publication != null)
        {
            try
            {
                await this.publication.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Nothing useful to do about a failed withdrawal - the record ages out of caches anyway.
            }
            this.publication = null;
        }

        this.IsRunning = false;
        this.Status = "Stopped";
        this.MdnsStatus = "Not published";
        this.Endpoints = [];
    }

    async Task Publish(IObdHostSink sink)
    {
        try
        {
            this.publication = await mdns
                .Publish(new MdnsServiceRegistration(config.MdnsInstanceName, config.MdnsServiceType, config.TcpPort)
                {
                    // A browsing client can tell what it found - and which dialect to speak - without
                    // having to connect and probe with ATI first.
                    TxtRecords = new Dictionary<string, string>
                    {
                        ["protocol"] = "elm327",
                        ["model"] = "Shiny.Obd emulator",
                        ["transport"] = "tcp"
                    }
                })
                .ConfigureAwait(true);

            this.MdnsStatus = $"Published {config.MdnsInstanceName}.{config.MdnsServiceType} on port {config.TcpPort}";
        }
        catch (Exception ex)
        {
            this.MdnsStatus = $"mDNS publish failed: {ex.Message}";
            sink.Failed("mDNS", ex);
        }
    }

    async Task AcceptLoop(IObdHostSink sink, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && this.listener != null)
        {
            TcpClient client;
            try
            {
                client = await this.listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    sink.Failed("TCP", ex);

                return;
            }

            var task = this.Serve(client, sink, ct);
            lock (this.clientTasks)
            {
                this.clientTasks.RemoveAll(x => x.IsCompleted);
                this.clientTasks.Add(task);
            }
        }
    }

    async Task Serve(TcpClient client, IObdHostSink sink, CancellationToken ct)
    {
        var address = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        var hosted = new HostedClient
        {
            Id = address,
            Transport = "TCP",
            Address = address
        };

        var session = new Elm327Session(address);
        sink.ClientConnected(hosted);

        try
        {
            client.NoDelay = true;

            using var _ = client;
            await using var stream = client.GetStream();

            var buffer = new byte[512];
            var pending = new StringBuilder();

            while (!ct.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read == 0)
                    break;

                pending.Append(Encoding.ASCII.GetString(buffer, 0, read));

                // Same framing rule as the BLE side: a command ends at the carriage return, and a
                // client is free to send several at once or split one across packets.
                var text = pending.ToString();
                var end = text.LastIndexOf('\r');
                if (end < 0)
                    continue;

                pending.Clear();
                pending.Append(text[(end + 1)..]);

                foreach (var command in text[..end].Split('\r', StringSplitOptions.RemoveEmptyEntries))
                {
                    var exchange = responder.Respond(session, command);
                    sink.Exchanged(hosted, exchange);

                    var response = Encoding.ASCII.GetBytes(session.Render(exchange));
                    await stream.WriteAsync(response, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            sink.Failed("TCP", ex);
        }
        finally
        {
            sink.ClientDisconnected(hosted.Id);
        }
    }

    /// <summary>Every usable IPv4 address on this device, so the UI can tell you where to point a client.</summary>
    static IEnumerable<IPAddress> LocalAddresses()
        => NetworkInterface
            .GetAllNetworkInterfaces()
            .Where(x => x.OperationalStatus == OperationalStatus.Up)
            .Where(x => x.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(x => x.GetIPProperties().UnicastAddresses)
            .Select(x => x.Address)
            .Where(x => x.AddressFamily == AddressFamily.InterNetwork)
            .Where(x => !IPAddress.IsLoopback(x));
}
