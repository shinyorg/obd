using System.Net;
using System.Net.Sockets;
using System.Text;
using Shiny.Obd;
using Shiny.Obd.Commands;
using Shiny.Obd.Wifi;

namespace Shiny.Obd.Tests;

/// <summary>
/// A loopback stand-in for an ELM327 WiFi adapter.
/// </summary>
/// <remarks>
/// Worth the ~80 lines: the WiFi transport is the one transport whose real device can be simulated
/// exactly, because the wire protocol *is* a TCP socket. Everything the serial and BLE tests have to
/// assert indirectly - prompt framing, a reply split across packets, a late reply after a timeout, a
/// mid-command disconnect - is directly reproducible here.
/// </remarks>
sealed class FakeElmAdapter : IAsyncDisposable
{
    readonly TcpListener listener;
    readonly CancellationTokenSource cts = new();
    readonly Func<string, Task<string?>> responder;
    readonly Task loop;

    FakeElmAdapter(Func<string, Task<string?>> responder)
    {
        this.responder = responder;
        this.listener = new TcpListener(IPAddress.Loopback, 0);
        this.listener.Start();
        this.loop = Task.Run(this.Accept);
    }

    /// <summary>An adapter that answers every command with the same canned reply.</summary>
    public static FakeElmAdapter Always(string reply)
        => new(_ => Task.FromResult<string?>(reply));

    /// <summary>An adapter that answers per command; a null reply means "say nothing".</summary>
    public static FakeElmAdapter Responding(Func<string, Task<string?>> responder)
        => new(responder);

    /// <summary>An adapter that accepts the connection and then never says a word - a router, in effect.</summary>
    public static FakeElmAdapter Silent()
        => new(_ => Task.FromResult<string?>(null));

    public int Port => ((IPEndPoint)this.listener.LocalEndpoint).Port;

    public WifiObdEndpoint Endpoint => new("127.0.0.1", this.Port);

    public List<string> Received { get; } = [];

    /// <summary>Set to drop the connection instead of replying to the next command.</summary>
    public bool DropOnNextCommand { get; set; }

    async Task Accept()
    {
        while (!this.cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await this.listener.AcceptTcpClientAsync(this.cts.Token);
            }
            catch (Exception)
            {
                return;
            }

            _ = Task.Run(() => this.Serve(client));
        }
    }

    async Task Serve(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            var buffer = new byte[256];
            var pending = new StringBuilder();

            while (!this.cts.IsCancellationRequested)
            {
                int read;
                try
                {
                    read = await stream.ReadAsync(buffer, this.cts.Token);
                }
                catch (Exception)
                {
                    return;
                }

                if (read <= 0)
                    return;

                pending.Append(Encoding.ASCII.GetString(buffer, 0, read));

                // A real adapter acts on carriage return, so commands are framed that way here too.
                var text = pending.ToString();
                int cr;
                while ((cr = text.IndexOf('\r')) >= 0)
                {
                    var command = text[..cr];
                    text = text[(cr + 1)..];

                    lock (this.Received)
                        this.Received.Add(command);

                    if (this.DropOnNextCommand)
                        return;

                    var reply = await this.responder(command);
                    if (reply == null)
                        continue;

                    try
                    {
                        // The real framing: the reply, then a bare CR, then the prompt.
                        await stream.WriteAsync(Encoding.ASCII.GetBytes($"{reply}\r\r>"), this.cts.Token);
                        await stream.FlushAsync(this.cts.Token);
                    }
                    catch (Exception)
                    {
                        return;
                    }
                }

                pending.Clear();
                pending.Append(text);
            }
        }
    }

    /// <summary>Writes a reply one byte at a time so the transport must reassemble it across reads.</summary>
    public static async Task WriteSlowly(NetworkStream stream, string text, CancellationToken ct)
    {
        foreach (var c in text)
        {
            await stream.WriteAsync(new[] { (byte)c }, ct);
            await stream.FlushAsync(ct);
            await Task.Delay(5, ct);
        }
    }

    bool disposed;

    /// <summary>
    /// Idempotent - a test that hangs the adapter up mid-scenario still gets disposed again by its
    /// <c>await using</c>.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (this.disposed)
            return;

        this.disposed = true;
        await this.cts.CancelAsync();
        this.listener.Stop();

        try
        {
            await this.loop;
        }
        catch (Exception)
        {
        }

        this.cts.Dispose();
    }
}

public class WifiObdConfigurationTests
{
    [Fact]
    public void Defaults_MatchTheCommonAdapters()
    {
        var config = new WifiObdConfiguration();

        Assert.Equal(35000, config.Port);
        Assert.True(config.AutoDetectEndpoint);

        // An OBD exchange is a tiny write followed by a tiny read, which is exactly the traffic Nagle
        // delays - leaving it on costs tens of milliseconds on every PID read.
        Assert.True(config.NoDelay);
    }

    [Fact]
    public void EndpointCandidates_LeadWithTheObdLinkDefault()
    {
        var first = new WifiObdConfiguration().EndpointCandidates[0];

        Assert.Equal("192.168.0.10", first.Host);
        Assert.Equal(35000, first.Port);
    }

    [Fact]
    public void Candidates_PutAConfiguredHostFirst()
    {
        var candidates = WifiObdProbe.BuildCandidates(new WifiObdConfiguration
        {
            Host = "10.9.8.7",
            Port = 4321,
            IncludeGatewayCandidates = false
        });

        Assert.Equal(new WifiObdEndpoint("10.9.8.7", 4321), candidates[0]);
        Assert.True(candidates.Count > 1, "auto-detection should still fall back to the well-known list");
    }

    [Fact]
    public void Candidates_AreDistinct()
    {
        // The gateway heuristic routinely turns up an address that is already on the well-known list.
        // Probing it twice would double the time to fail on a network with no adapter at all.
        var candidates = WifiObdProbe.BuildCandidates(new WifiObdConfiguration());

        Assert.Equal(candidates.Count, candidates.Distinct().Count());
    }

    [Fact]
    public void Candidates_WithDetectionOff_AreJustTheConfiguredHost()
    {
        var candidates = WifiObdProbe.BuildCandidates(new WifiObdConfiguration
        {
            Host = "192.168.0.10",
            AutoDetectEndpoint = false
        });

        Assert.Single(candidates);
    }

    [Fact]
    public void DefaultGateways_DoesNotThrowOnAnyPlatform()
        => Assert.NotNull(WifiObdProbe.DefaultGateways());
}

public class WifiObdTransportTests
{
    static WifiObdConfiguration Fast(WifiObdEndpoint endpoint, bool detect = false) => new()
    {
        Host = endpoint.Host,
        Port = endpoint.Port,
        AutoDetectEndpoint = detect,
        IncludeGatewayCandidates = false,
        ConnectTimeout = TimeSpan.FromSeconds(2),
        ProbeTimeout = TimeSpan.FromMilliseconds(500),
        CommandTimeout = TimeSpan.FromSeconds(2),
        KeepAliveInterval = TimeSpan.Zero,
        EndpointCandidates = []
    };

    [Fact]
    public void NotConnected_BeforeConnect()
    {
        var transport = new WifiObdTransport("192.168.0.10");

        Assert.False(transport.IsConnected);
        Assert.Null(transport.ConnectedEndpoint);
    }

    [Fact]
    public async Task Send_ThrowsWhenNotConnected()
    {
        var transport = new WifiObdTransport("192.168.0.10");

        await Assert.ThrowsAsync<ObdException>(() => transport.Send("010D\r"));
    }

    [Fact]
    public async Task Connect_AndSend_StripsThePrompt()
    {
        await using var adapter = FakeElmAdapter.Always("41 0D 32");
        await using var transport = new WifiObdTransport(Fast(adapter.Endpoint));

        await transport.Connect();
        Assert.True(transport.IsConnected);
        Assert.Equal(adapter.Endpoint, transport.ConnectedEndpoint);

        var response = await transport.Send("010D\r");

        Assert.Equal("41 0D 32", response);
        Assert.Contains("010D", adapter.Received);
    }

    [Fact]
    public async Task Connect_WithDetectionOff_SendsNoProbe()
    {
        // An explicit host is a promise the adapter is there. Probing it anyway would break the one
        // case this path exists for: an adapter that dislikes ATI.
        await using var adapter = FakeElmAdapter.Always("OK");
        await using var transport = new WifiObdTransport(Fast(adapter.Endpoint));

        await transport.Connect();

        Assert.Empty(adapter.Received);
    }

    [Fact]
    public async Task Connect_WithDetection_ProbesWithAti()
    {
        await using var adapter = FakeElmAdapter.Always("ELM327 v1.5");
        await using var transport = new WifiObdTransport(Fast(adapter.Endpoint, detect: true));

        await transport.Connect();

        Assert.Equal("ATI", adapter.Received.Single());
        Assert.Equal("ELM327 v1.5", transport.DetectedIdentifier);
    }

    [Fact]
    public async Task Connect_SkipsACandidateThatAcceptsThenSaysNothing()
    {
        // The whole reason detection validates rather than trusting the connect: a router on the
        // subnet completes the TCP handshake and then never speaks.
        await using var router = FakeElmAdapter.Silent();
        await using var adapter = FakeElmAdapter.Always("ELM327 v1.5");

        var config = Fast(router.Endpoint, detect: true);
        config.EndpointCandidates = [adapter.Endpoint];

        await using var transport = new WifiObdTransport(config);
        await transport.Connect();

        Assert.Equal(adapter.Endpoint, transport.ConnectedEndpoint);
    }

    [Fact]
    public async Task Connect_WhenNothingAnswers_NamesEveryEndpointTried()
    {
        var config = new WifiObdConfiguration
        {
            Host = "127.0.0.1",
            Port = 1,
            AutoDetectEndpoint = true,
            IncludeGatewayCandidates = false,
            EndpointCandidates = [new WifiObdEndpoint("127.0.0.1", 2)],
            ConnectTimeout = TimeSpan.FromMilliseconds(500),
            ProbeTimeout = TimeSpan.FromMilliseconds(200)
        };

        await using var transport = new WifiObdTransport(config);
        var ex = await Assert.ThrowsAsync<ObdException>(() => transport.Connect());

        // "Connect failed" is not actionable in a car park. The list of what was tried is.
        Assert.Contains("127.0.0.1:1", ex.Message, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1:2", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connect_ToARefusedPort_SaysItIsTheWrongPort()
    {
        // Reachable host, nothing listening. Almost always the 35000-vs-23 split, and the message
        // should say so rather than repeat the socket error code.
        var config = Fast(new WifiObdEndpoint("127.0.0.1", 1));
        await using var transport = new WifiObdTransport(config);

        var ex = await Assert.ThrowsAsync<ObdException>(() => transport.Connect());

        Assert.Contains("35000", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_ReassemblesAReplySplitAcrossPackets()
    {
        // TCP gives no guarantee of one read per write, and a multi-frame CAN reply genuinely does
        // arrive in pieces. Completion is on the prompt, not on a read boundary.
        await using var adapter = FakeElmAdapter.Responding(async command =>
        {
            await Task.Delay(1);
            return command == "0902" ? "014\r0: 49 02 01 57 42 41\r1: 31 32 33 34 35 36 37" : "OK";
        });

        await using var transport = new WifiObdTransport(Fast(adapter.Endpoint));
        await transport.Connect();

        var response = await transport.Send("0902\r");

        Assert.Contains("0: 49 02 01 57 42 41", response);
        Assert.Contains("1: 31 32 33 34 35 36 37", response);
        Assert.DoesNotContain('>', response);
    }

    [Fact]
    public async Task Send_WhenTheAdapterGoesQuiet_ThrowsObdTimeout()
    {
        await using var adapter = FakeElmAdapter.Silent();
        await using var transport = new WifiObdTransport(Fast(adapter.Endpoint));

        await transport.Connect();

        var ex = await Assert.ThrowsAsync<ObdTimeoutException>(() => transport.Send("010D\r"));
        Assert.Equal("010D", ex.Command);
    }

    [Fact]
    public async Task Send_AfterATimeout_IsNotAnsweredByTheLateReply()
    {
        // The failure this guards is silent and permanent: without it, a single timeout puts every
        // subsequent response off by one and the session never recovers on its own.
        var gate = new TaskCompletionSource();

        await using var adapter = FakeElmAdapter.Responding(async command =>
        {
            if (command == "SLOW")
            {
                await gate.Task;
                return "LATE";
            }
            return command == "FAST" ? "41 0D 32" : "OK";
        });

        var config = Fast(adapter.Endpoint);
        config.CommandTimeout = TimeSpan.FromMilliseconds(300);

        await using var transport = new WifiObdTransport(config);
        await transport.Connect();

        await Assert.ThrowsAsync<ObdTimeoutException>(() => transport.Send("SLOW\r"));

        // Release the stale reply, then ask a fresh question. The answer must be the fresh one.
        gate.SetResult();
        await Task.Delay(100);

        Assert.Equal("41 0D 32", await transport.Send("FAST\r"));
    }

    [Fact]
    public async Task Send_WhenTheAdapterDropsTheSocket_FailsImmediately()
    {
        // Rather than leaving the caller to sit out the full command timeout for a reply that can
        // never arrive. Clone firmware really does drop connections mid-session.
        await using var adapter = FakeElmAdapter.Always("OK");
        await using var transport = new WifiObdTransport(Fast(adapter.Endpoint));

        await transport.Connect();
        adapter.DropOnNextCommand = true;

        var ex = await Assert.ThrowsAsync<ObdException>(() => transport.Send("010D\r"));

        Assert.IsNotType<ObdTimeoutException>(ex);
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task IsConnected_GoesFalseWhenTheAdapterHangsUpWhileIdle()
    {
        // Socket.Connected would still report true here - it reflects the last I/O, not the current
        // state - which is why the transport tracks the link from its read pump instead.
        await using var adapter = FakeElmAdapter.Always("OK");
        await using var transport = new WifiObdTransport(Fast(adapter.Endpoint));

        await transport.Connect();
        Assert.True(transport.IsConnected);

        await adapter.DisposeAsync();

        var deadline = Environment.TickCount64 + 3000;
        while (transport.IsConnected && Environment.TickCount64 < deadline)
            await Task.Delay(25);

        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task KeepAlive_PollsTheAdapterWhileIdle()
    {
        // Without this an app that connects and then waits for the user finds out the socket died
        // only when the user finally presses something.
        await using var adapter = FakeElmAdapter.Always("ELM327 v1.5");

        var config = Fast(adapter.Endpoint);
        config.KeepAliveInterval = TimeSpan.FromMilliseconds(200);

        await using var transport = new WifiObdTransport(config);
        await transport.Connect();

        await Task.Delay(1200);

        lock (adapter.Received)
            Assert.Contains("ATI", adapter.Received);
    }

    [Fact]
    public async Task Disconnect_LeavesTheTransportReusable()
    {
        await using var adapter = FakeElmAdapter.Always("41 0D 32");
        await using var transport = new WifiObdTransport(Fast(adapter.Endpoint));

        await transport.Connect();
        await transport.Disconnect();

        Assert.False(transport.IsConnected);
        Assert.Null(transport.ConnectedEndpoint);

        // A dropped link is not fatal - the documented recovery is to connect again, which is only
        // honest if the transport can actually be reconnected.
        await transport.Connect();
        Assert.Equal("41 0D 32", await transport.Send("010D\r"));
    }

    [Fact]
    public async Task DisposeAsync_IsSafeBeforeConnect()
    {
        var transport = new WifiObdTransport("192.168.0.10");
        await transport.DisposeAsync();
    }

    [Fact]
    public async Task Construction_FromForeignDiscoveredDevice_ExplainsTheMismatch()
    {
        var device = new ObdDiscoveredDevice("Some BLE thing", "abc", new object());

        var ex = Assert.Throws<ObdException>(() => new WifiObdTransport(device, new WifiObdConfiguration()));
        Assert.Contains("WifiObdDeviceScanner", ex.Message, StringComparison.Ordinal);

        await Task.CompletedTask;
    }
}

public class WifiObdConnectionTests
{
    [Fact]
    public async Task FullStack_DetectsTheAdapterAndParsesAReading()
    {
        // The only place in the suite where the whole chain - socket, prompt framing, adapter
        // detection, profile init, hex parsing - runs against something that behaves like a real
        // adapter. The WiFi transport is what makes that possible: its wire protocol *is* a socket.
        await using var adapter = FakeElmAdapter.Responding(command => Task.FromResult<string?>(command switch
        {
            "ATI" => "ELM327 v1.5",
            "010D" => "41 0D 32",       // 0x32 == 50 km/h
            "0902" => "014\r0: 49 02 01 57 42 41\r1: 55 5A 5A 5A 30 35 42\r2: 33 30 30 30 30 30 31",
            _ => "OK"
        }));

        await using var transport = new WifiObdTransport(new WifiObdConfiguration
        {
            Host = adapter.Endpoint.Host,
            Port = adapter.Endpoint.Port,
            AutoDetectEndpoint = false,
            CommandTimeout = TimeSpan.FromSeconds(2),
            KeepAliveInterval = TimeSpan.Zero
        });

        await using var connection = new ObdConnection(transport);
        await connection.Connect();

        Assert.Equal(ObdAdapterType.Elm327, connection.DetectedAdapter?.Type);
        Assert.Equal(50, await connection.Execute(StandardCommands.VehicleSpeed));

        // Multi-frame: the byte-count line and the "N:" indexes are framing, not payload.
        Assert.Equal("WBAUZZZ05B3000001", await connection.Execute(StandardCommands.Vin));

        // The profile really did run against the adapter rather than being assumed.
        lock (adapter.Received)
            Assert.Contains("ATE0", adapter.Received);
    }
}

public class WifiObdDeviceScannerTests
{
    static WifiObdConfiguration Probing(params WifiObdEndpoint[] candidates) => new()
    {
        Host = null,
        IncludeGatewayCandidates = false,
        EndpointCandidates = candidates,
        ConnectTimeout = TimeSpan.FromMilliseconds(500),
        ProbeTimeout = TimeSpan.FromMilliseconds(500)
    };

    [Fact]
    public async Task Scan_CompletesOnCancellation()
    {
        var scanner = new WifiObdDeviceScanner(Probing(new WifiObdEndpoint("127.0.0.1", 1)));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await scanner.Scan(_ => { }, cts.Token);
    }

    [Fact]
    public async Task Scan_ReportsAnAdapterThatAnswers()
    {
        await using var adapter = FakeElmAdapter.Always("ELM327 v1.5");

        var scanner = new WifiObdDeviceScanner(Probing(adapter.Endpoint));
        var found = new List<ObdDiscoveredDevice>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await scanner.Scan(found.Add, cts.Token);

        var device = Assert.Single(found);
        Assert.Equal("ELM327 v1.5", device.Name);
        Assert.Equal(adapter.Endpoint.ToString(), device.Id);
        Assert.Equal(adapter.Endpoint, Assert.IsType<WifiObdEndpoint>(device.NativeDevice));
    }

    [Fact]
    public async Task Scan_IgnoresAnEndpointThatAcceptsButSaysNothing()
    {
        await using var router = FakeElmAdapter.Silent();

        var scanner = new WifiObdDeviceScanner(Probing(router.Endpoint));
        var found = new List<ObdDiscoveredDevice>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await scanner.Scan(found.Add, cts.Token);

        Assert.Empty(found);
    }

    [Fact]
    public async Task Scan_ReportsEachAdapterOnce()
    {
        // The scan re-probes on an interval so a picker UI stays live; an adapter still present on
        // the second pass must not be reported again or the list grows without bound.
        await using var adapter = FakeElmAdapter.Always("ELM327 v1.5");

        var scanner = new WifiObdDeviceScanner(Probing(adapter.Endpoint));
        var found = new List<ObdDiscoveredDevice>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));

        await scanner.Scan(found.Add, cts.Token);

        Assert.Single(found);
    }

    [Fact]
    public async Task Probe_ClosesItsSocketBeforeReturning()
    {
        // Most WiFi adapters accept exactly one TCP client. A scanner that held its probe open would
        // lock out the transport that connects immediately afterwards.
        await using var adapter = FakeElmAdapter.Always("ELM327 v1.5");

        var scanner = new WifiObdDeviceScanner(Probing(adapter.Endpoint));
        var found = new List<ObdDiscoveredDevice>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await scanner.Scan(found.Add, cts.Token);

        var device = Assert.Single(found);
        await using var transport = new WifiObdTransport(device, new WifiObdConfiguration
        {
            ConnectTimeout = TimeSpan.FromSeconds(2),
            CommandTimeout = TimeSpan.FromSeconds(2),
            KeepAliveInterval = TimeSpan.Zero
        });

        await transport.Connect();
        Assert.True(transport.IsConnected);
    }
}
