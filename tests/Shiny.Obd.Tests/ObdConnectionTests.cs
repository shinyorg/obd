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
        // Simulated multi-frame CAN response for VIN
        var vin = "WBA12345678901234";
        var vinHex = string.Join(" ", System.Text.Encoding.ASCII.GetBytes(vin).Select(b => b.ToString("X2")));
        var response = $"0: 49 02 01 {vinHex.Substring(0, 14)}\r1: {vinHex.Substring(15)}";

        var transport = new FakeTransport(response);
        var connection = new ObdConnection(transport);

        var result = await connection.Execute(StandardCommands.Vin);
        Assert.Equal(vin, result);
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
}
