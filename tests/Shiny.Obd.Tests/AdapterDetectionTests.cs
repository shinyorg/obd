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

        // Auto-detect sends ATZ + ATI first, then profile re-sends ATZ + rest
        Assert.Contains("ATZ\r", transport.SentCommands);
        Assert.Contains("ATI\r", transport.SentCommands);
        Assert.Contains("ATE0\r", transport.SentCommands);
        Assert.Contains("ATL0\r", transport.SentCommands);
        Assert.Contains("ATS1\r", transport.SentCommands);
        Assert.Contains("ATH0\r", transport.SentCommands);
        Assert.Contains("ATSP0\r", transport.SentCommands);
    }

    [Fact]
    public async Task Connect_ObdLink_SendsStnCommands()
    {
        var transport = new RecordingTransport(atiResponse: "STN2120 v5.0");
        var connection = new ObdConnection(transport);

        await connection.Connect();

        // Should include STN-specific commands after standard init
        Assert.Contains("STFAC\r", transport.SentCommands);
        Assert.Contains("ATCAF1\r", transport.SentCommands);
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

    /// <summary>
    /// Transport that records all commands and returns canned ATI response
    /// </summary>
    class RecordingTransport : IObdTransport
    {
        readonly string atiResponse;
        public List<string> SentCommands { get; } = new();
        public bool IsConnected => true;

        public RecordingTransport(string atiResponse) => this.atiResponse = atiResponse;

        public Task Connect(CancellationToken ct = default) => Task.CompletedTask;
        public Task Disconnect() => Task.CompletedTask;

        public Task<string> Send(string command, CancellationToken ct = default)
        {
            SentCommands.Add(command);

            if (command.TrimEnd('\r') == "ATI")
                return Task.FromResult(atiResponse);

            return Task.FromResult("OK");
        }

        public ValueTask DisposeAsync() => default;
    }
}
