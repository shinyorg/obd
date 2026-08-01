using Shiny.Obd.Commands;

namespace Shiny.Obd.Tests;

public class ObdConnectionTests
{
    [Fact]
    public async Task Execute_ParsesSingleLineResponse()
    {
        var transport = new FakeTransport("41 0D 50");
        var connection = new ObdConnection(transport);

        var speed = await connection.Execute(StandardCommands.VehicleSpeed);
        Assert.Equal(80, speed);
    }

    [Fact]
    public async Task Execute_ParsesMultiLineResponse()
    {
        // Exactly what an ELM327 prints for 0902, byte-count line and all. The count is framing:
        // 0x014 = 20 = the 49 02 01 header plus 17 VIN characters.
        var response =
            "014\r" +
            "0: 49 02 01 57 42 41\r" +
            "1: 31 32 33 34 35 36 37\r" +
            "2: 38 39 30 31 32 33 34";

        var transport = new FakeTransport(response);
        var connection = new ObdConnection(transport);

        var result = await connection.Execute(StandardCommands.Vin);
        Assert.Equal("WBA12345678901234", result);
    }

    [Fact]
    public async Task Execute_IgnoresMultiFrameByteCountLine()
    {
        // The regression this guards: "014" parses cleanly as 0x14, so treating it as payload shifts
        // every byte along by one and the mode echo check rejects an otherwise good reply. Both
        // spellings of the count line an adapter may print are covered.
        foreach (var count in new[] { "014", "14" })
        {
            var response =
                $"{count}\r" +
                "0: 49 02 01 57 42 41\r" +
                "1: 31 32 33 34 35 36 37\r" +
                "2: 38 39 30 31 32 33 34";

            var connection = new ObdConnection(new FakeTransport(response));

            var result = await connection.Execute(StandardCommands.Vin);
            Assert.Equal("WBA12345678901234", result);
        }
    }

    [Fact]
    public async Task Execute_ParsesMultiFrameWithoutSpaces()
    {
        // The profiles ask for spaces (ATS1) but clones ignore it. An unspaced run used to fail to
        // parse as a single token and be dropped whole, losing the response rather than reporting it.
        var response =
            "014\r" +
            "0:490201574241\r" +
            "1:31323334353637\r" +
            "2:38393031323334";

        var connection = new ObdConnection(new FakeTransport(response));

        var result = await connection.Execute(StandardCommands.Vin);
        Assert.Equal("WBA12345678901234", result);
    }

    [Fact]
    public async Task Execute_SingleFrameResponseHasNoCountLineToStrip()
    {
        // A single-frame reply carries no count and no frame numbers, which is why only the
        // multi-frame commands were ever affected. Pinned so the fix cannot regress the common path.
        var connection = new ObdConnection(new FakeTransport("41 0C 1A F8"));

        var rpm = await connection.Execute(StandardCommands.EngineRpm);
        Assert.Equal(1726, rpm);
    }

    [Fact]
    public async Task Execute_ThrowsOnNoData()
    {
        var transport = new FakeTransport("NO DATA");
        var connection = new ObdConnection(transport);

        await Assert.ThrowsAsync<ObdException>(
            () => connection.Execute(StandardCommands.VehicleSpeed));
    }

    [Fact]
    public async Task Execute_ThrowsOnUnableToConnect()
    {
        var transport = new FakeTransport("UNABLE TO CONNECT");
        var connection = new ObdConnection(transport);

        await Assert.ThrowsAsync<ObdException>(
            () => connection.Execute(StandardCommands.VehicleSpeed));
    }

    [Fact]
    public async Task Execute_ThrowsOnEmptyResponse()
    {
        var transport = new FakeTransport("  ");
        var connection = new ObdConnection(transport);

        await Assert.ThrowsAsync<ObdException>(
            () => connection.Execute(StandardCommands.VehicleSpeed));
    }

    [Fact]
    public async Task Execute_StripsSearchingPrefix()
    {
        var transport = new FakeTransport("SEARCHING...\r41 0D 50");
        var connection = new ObdConnection(transport);

        var speed = await connection.Execute(StandardCommands.VehicleSpeed);
        Assert.Equal(80, speed);
    }

    [Fact]
    public async Task SendRaw_AppendsCarriageReturn()
    {
        var transport = new FakeTransport("OK");
        var connection = new ObdConnection(transport);

        await connection.SendRaw("ATZ");
        Assert.Equal("ATZ\r", transport.LastCommand);
    }

    [Fact]
    public async Task Execute_SurfacesTransportTimeoutAsObdTimeout()
    {
        var transport = new ThrowingTransport(new ObdTimeoutException("010D", TimeSpan.FromSeconds(10)));
        var connection = new ObdConnection(transport);

        var ex = await Assert.ThrowsAsync<ObdTimeoutException>(
            () => connection.Execute(StandardCommands.VehicleSpeed));

        Assert.Equal("010D", ex.Command);
        Assert.Equal(TimeSpan.FromSeconds(10), ex.Timeout);
    }

    [Fact]
    public async Task Execute_TimeoutIsNotACancellation()
    {
        // A caller polling in a loop keys off cancellation to know it is shutting down. If an adapter
        // going quiet arrived as an OperationCanceledException the two would be indistinguishable and
        // one slow reply would tear the loop down.
        var transport = new ThrowingTransport(new ObdTimeoutException("010C", TimeSpan.FromSeconds(1)));
        var connection = new ObdConnection(transport);

        var ex = await Record.ExceptionAsync(() => connection.Execute(StandardCommands.EngineRpm));

        Assert.IsNotType<OperationCanceledException>(ex, exactMatch: false);
        Assert.IsType<ObdException>(ex, exactMatch: false);
    }

    [Fact]
    public async Task Execute_PropagatesCallerCancellation()
    {
        var transport = new ThrowingTransport(new OperationCanceledException());
        var connection = new ObdConnection(transport);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => connection.Execute(StandardCommands.VehicleSpeed));
    }

    /// <summary>
    /// Minimal fake transport that returns a canned response
    /// </summary>
    class FakeTransport : IObdTransport
    {
        readonly string response;
        public string? LastCommand { get; private set; }
        public bool IsConnected => true;

        public FakeTransport(string response) => this.response = response;

        public Task Connect(CancellationToken ct = default) => Task.CompletedTask;
        public Task Disconnect() => Task.CompletedTask;

        public Task<string> Send(string command, CancellationToken ct = default)
        {
            LastCommand = command;
            return Task.FromResult(response);
        }

        public ValueTask DisposeAsync() => default;
    }

    /// <summary>
    /// Fake transport that fails every send with a given exception
    /// </summary>
    class ThrowingTransport : IObdTransport
    {
        readonly Exception error;
        public bool IsConnected => true;

        public ThrowingTransport(Exception error) => this.error = error;

        public Task Connect(CancellationToken ct = default) => Task.CompletedTask;
        public Task Disconnect() => Task.CompletedTask;
        public Task<string> Send(string command, CancellationToken ct = default) => Task.FromException<string>(this.error);
        public ValueTask DisposeAsync() => default;
    }
}
