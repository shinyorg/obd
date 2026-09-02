namespace Shiny.Obd.Tests;

public class AdapterDetectionTests
{
    [Fact]
    public async Task Connect_DetectsElm327()
    {
        var transport = new RecordingTransport(atiResponse: "ELM327 v1.5");
        var connection = new ObdConnection(transport);

        await connection.Connect();

        Assert.NotNull(connection.DetectedAdapter);
        Assert.Equal(ObdAdapterType.Elm327, connection.DetectedAdapter!.Type);
        Assert.Contains("ELM327", connection.DetectedAdapter.RawIdentifier);
    }

    [Fact]
    public async Task Connect_DetectsObdLink()
    {
        var transport = new RecordingTransport(atiResponse: "STN1110 v4.2.1");
        var connection = new ObdConnection(transport);

        await connection.Connect();

        Assert.NotNull(connection.DetectedAdapter);
        Assert.Equal(ObdAdapterType.ObdLink, connection.DetectedAdapter!.Type);
        Assert.Contains("STN1110", connection.DetectedAdapter.RawIdentifier);
    }

    [Fact]
    public async Task Connect_DetectsUnknownAdapter()
    {
        var transport = new RecordingTransport(atiResponse: "CUSTOM_ADAPTER v1.0");
        var connection = new ObdConnection(transport);

        await connection.Connect();

        Assert.NotNull(connection.DetectedAdapter);
        Assert.Equal(ObdAdapterType.Unknown, connection.DetectedAdapter!.Type);
    }

    [Fact]
    public async Task Connect_Elm327_SendsCorrectInitSequence()
    {
        var transport = new RecordingTransport(atiResponse: "ELM327 v1.5");
        var connection = new ObdConnection(transport);

        await connection.Connect();

        Assert.Contains("ATI\r", transport.SentCommands);
        Assert.Contains("ATZ\r", transport.SentCommands);
        Assert.Contains("ATE0\r", transport.SentCommands);
        Assert.Contains("ATL0\r", transport.SentCommands);
        Assert.Contains("ATS1\r", transport.SentCommands);
        Assert.Contains("ATH0\r", transport.SentCommands);
        Assert.Contains("ATSP0\r", transport.SentCommands);
    }

    [Fact]
    public async Task Connect_ResetsTheAdapterExactlyOnce()
    {
        // The regression this guards: auto-detect used to send its own ATZ before probing with ATI,
        // and the profile then sent a second one. Two full adapter resets and two one-second settles on
        // every connect, with the second reset discarding what the first sequence had established.
        var transport = new RecordingTransport(atiResponse: "ELM327 v1.5");
        var connection = new ObdConnection(transport);

        await connection.Connect();

        Assert.Single(transport.SentCommands, x => x == "ATZ\r");
    }

    [Fact]
    public async Task Connect_ObdLink_SendsStnCommands()
    {
        var transport = new RecordingTransport(atiResponse: "STN2120 v5.0");
        var connection = new ObdConnection(transport);

        await connection.Connect();

        Assert.Contains("STFAC\r", transport.SentCommands);
        Assert.Contains("ATCAF1\r", transport.SentCommands);
    }

    [Fact]
    public async Task Connect_ObdLink_ResetsToFactoryDefaultsBeforeConfiguringTheAdapter()
    {
        // STFAC restores factory defaults, so running it after the ELM configuration wiped
        // ATE0/ATL0/ATS1/ATH0 and the protocol selection in the same breath as setting them.
        var transport = new RecordingTransport(atiResponse: "STN2120 v5.0");
        var connection = new ObdConnection(transport);

        await connection.Connect();

        Assert.True(
            transport.SentCommands.IndexOf("STFAC\r") < transport.SentCommands.IndexOf("ATE0\r"),
            "STFAC must precede the ELM327 configuration it would otherwise reset"
        );
    }

    [Fact]
    public async Task Connect_ExplicitProfile_SkipsDetection()
    {
        var transport = new RecordingTransport(atiResponse: "anything");
        var profile = new Elm327AdapterProfile();
        var connection = new ObdConnection(transport, profile);

        await connection.Connect();

        Assert.Null(connection.DetectedAdapter);
        // ATI should NOT be sent when profile is explicit
        Assert.DoesNotContain("ATI\r", transport.SentCommands);
        Assert.Contains("ATZ\r", transport.SentCommands);
    }

    [Fact]
    public async Task Connect_Unpinned_SearchesWithoutProbing()
    {
        // Nothing is being verified when there is no pin, so the probe would be a round trip spent to
        // learn nothing — and a bus that is asleep with the ignition off must not fail the connect.
        var transport = new RecordingTransport(atiResponse: "ELM327 v1.5");
        var connection = new ObdConnection(transport);

        await connection.Connect();

        Assert.Contains("ATSP0\r", transport.SentCommands);
        Assert.DoesNotContain("0100\r", transport.SentCommands);
    }

    [Fact]
    public async Task Connect_PinnedProtocol_SkipsTheSearch()
    {
        var transport = new RecordingTransport(atiResponse: "ELM327 v1.5");
        transport.Responds("0100", "41 00 BE 3E B8 11");
        var connection = new ObdConnection(transport) { Protocol = "6" };

        await connection.Connect();

        Assert.Contains("ATSP6\r", transport.SentCommands);
        Assert.DoesNotContain("ATSP0\r", transport.SentCommands);
    }

    [Fact]
    public async Task Connect_PinnedProtocol_FallsBackToSearchWhenNothingAnswers()
    {
        // A pin can be stale — a different vehicle, or a number learned on a bad session — and a wrong
        // protocol simply never answers. Without the fallback that is a session that can never read a
        // single PID and no way back but forgetting the adapter.
        var transport = new RecordingTransport(atiResponse: "ELM327 v1.5");
        transport.Responds("0100", "NO DATA");
        var connection = new ObdConnection(transport) { Protocol = "3" };

        await connection.Connect();

        Assert.Contains("ATSP3\r", transport.SentCommands);
        Assert.Contains("ATSP0\r", transport.SentCommands);
    }

    [Fact]
    public async Task Connect_PinnedProtocol_AcceptsAnUnspacedProbeReply()
    {
        // ATS1 is a request and clones ignore it, so the probe cannot depend on the spacing
        var transport = new RecordingTransport(atiResponse: "ELM327 v1.5");
        transport.Responds("0100", "4100BE3EB811");
        var connection = new ObdConnection(transport) { Protocol = "6" };

        await connection.Connect();

        Assert.DoesNotContain("ATSP0\r", transport.SentCommands);
    }

    [Theory]
    [InlineData("A6", "6")]     // found by searching
    [InlineData("6", "6")]      // set explicitly
    [InlineData("A", "A")]      // J1939 — a bare protocol number that is itself the auto prefix
    [InlineData("A0", null)]    // searching, nothing settled on yet
    [InlineData("0", null)]
    [InlineData("OK", null)]    // an adapter that doesn't implement ATDPN
    public async Task Connect_ReportsTheNegotiatedProtocol(string atdpn, string? expected)
    {
        var transport = new RecordingTransport(atiResponse: "ELM327 v1.5");
        transport.Responds("ATDPN", atdpn);
        var connection = new ObdConnection(transport);

        await connection.Connect();

        Assert.Equal(expected, connection.NegotiatedProtocol);
    }

    /// <summary>
    /// Transport that records all commands and answers from a scripted table, defaulting to "OK"
    /// </summary>
    class RecordingTransport : IObdTransport
    {
        readonly Dictionary<string, string> responses = new();
        public List<string> SentCommands { get; } = new();
        public bool IsConnected => true;

        public RecordingTransport(string atiResponse) => this.responses["ATI"] = atiResponse;

        public RecordingTransport Responds(string command, string response)
        {
            this.responses[command] = response;
            return this;
        }

        public Task Connect(CancellationToken ct = default) => Task.CompletedTask;
        public Task Disconnect() => Task.CompletedTask;

        public Task<string> Send(string command, CancellationToken ct = default)
        {
            SentCommands.Add(command);

            return Task.FromResult(
                this.responses.TryGetValue(command.TrimEnd('\r'), out var response) ? response : "OK"
            );
        }

        public ValueTask DisposeAsync() => default;
    }
}
