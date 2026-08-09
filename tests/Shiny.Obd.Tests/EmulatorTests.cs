using Shiny.Obd.Commands;
using Shiny.Obd.Emulator;
using Shiny.Obd.Wifi;

namespace Shiny.Obd.Tests;

/// <summary>
/// Drives the emulator with the real client stack — <see cref="ObdConnection"/> over
/// <see cref="WifiObdTransport"/> — rather than calling <see cref="Elm327Responder"/> directly.
/// </summary>
/// <remarks>
/// That is the whole point of these: the emulator exists so an app can be tested without a car, and
/// the only way to know it is fit for that is to make the library talk to it over a socket, through
/// the same initialisation handshake, framing and parsing an app would use. A responder unit test
/// would pass happily while the thing was unusable end to end.
/// </remarks>
public class EmulatorTests : IAsyncLifetime
{
    ObdEmulatorState state = null!;
    TcpObdServer server = null!;
    ObdEmulatorHost host = null!;

    public async Task InitializeAsync()
    {
        // Port 0: the OS picks a free one, so these can run alongside anything else - including a real
        // emulator on 35000.
        var config = new ObdEmulatorConfiguration { TcpPort = 0 };

        this.state = new ObdEmulatorState();
        this.server = new TcpObdServer(new Elm327Responder(this.state), config);
        this.host = new ObdEmulatorHost(
            [this.server],
            config,

            // No SynchronizationContext in a test host, so everything runs inline - which is what
            // makes the assertions below safe to make straight after an await.
            new SynchronizationContextDispatcher(null)
        );

        await this.host.Start();
        Assert.True(this.server.IsRunning, this.server.Status);
    }

    public async Task DisposeAsync() => await this.host.Stop();

    async Task<IObdConnection> Connect()
    {
        var connection = new ObdConnection(new WifiObdTransport("127.0.0.1", this.server.BoundPort));
        await connection.Connect();
        return connection;
    }

    [Fact]
    public async Task Connects_AndDetectsTheAdapter()
    {
        await using var connection = await this.Connect();

        Assert.True(connection.IsConnected);
        Assert.Contains("ELM327", await connection.SendRaw("ATI"));
    }

    [Fact]
    public async Task ReadsBackAValueSetOnTheEmulator()
    {
        this.state.Find(0x01, 0x0D)!.Number = 88;
        this.state.Find(0x01, 0x0C)!.Number = 2400;

        await using var connection = await this.Connect();

        Assert.Equal(88, await connection.Execute(StandardCommands.VehicleSpeed));
        Assert.Equal(2400, await connection.Execute(StandardCommands.EngineRpm));
    }

    /// <summary>
    /// A VIN is 17 characters, which does not fit one CAN frame - so this is also the multi-frame
    /// reassembly path, the one most likely to be broken by a framing change on either side.
    /// </summary>
    [Fact]
    public async Task ReadsTheSelectedVehiclesVin()
    {
        this.state.Vehicle = VehicleCatalog.ToyotaCamry;

        await using var connection = await this.Connect();

        Assert.Equal(VehicleCatalog.ToyotaCamry.Vin, await connection.Execute(StandardCommands.Vin));
    }

    /// <summary>
    /// Every catalog VIN has to survive the round trip, because a decoder is the next thing an app
    /// does with one and a mangled character makes it "no such vehicle" rather than an obvious fault.
    /// </summary>
    [Fact]
    public async Task EveryCatalogVehiclesVinSurvivesTheRoundTrip()
    {
        await using var connection = await this.Connect();

        foreach (var vehicle in VehicleCatalog.All.Where(x => x.ReportsVehicleInformation))
        {
            this.state.Vehicle = vehicle;
            Assert.Equal(vehicle.Vin, await connection.Execute(StandardCommands.Vin));
        }
    }

    /// <summary>
    /// The case an app that keys vehicles by VIN has to survive: a vehicle old enough to answer no
    /// mode 09 at all. It must fail as "no data", not as a parse error or a hang.
    /// </summary>
    [Fact]
    public async Task AVehicleWithoutModeNineHasNoVinToRead()
    {
        this.state.Vehicle = VehicleCatalog.ChevroletCavalier;
        Assert.False(this.state.Vehicle.ReportsVehicleInformation);

        await using var connection = await this.Connect();

        Assert.Contains("NO DATA", await connection.SendRaw("0902"), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Taking a PID away is the most useful thing the emulator does, so it has to be taken away
    /// properly: absent from the supported-PID mask a client walks, as well as unanswered.
    /// </summary>
    [Fact]
    public async Task AnElectricVehicleDropsTheEnginePids()
    {
        this.state.Vehicle = VehicleCatalog.NissanLeaf;

        await using var connection = await this.Connect();

        // Engine RPM (010C) is meaningless on a BEV and is one of the PIDs the Leaf drops.
        Assert.Contains("NO DATA", await connection.SendRaw("010C"), StringComparison.OrdinalIgnoreCase);

        // 0100 reports support for PIDs 01-20 as a bitmask; 0x0C is bit 12, counting from the MSB.
        var mask = await connection.SendRaw("0100");
        Assert.False(SupportsPid(mask, 0x0C), $"010C should be unsupported on the Leaf: {mask}");
    }

    [Fact]
    public async Task SwitchingVehicleChangesWhatTheBusAnswers()
    {
        await using var connection = await this.Connect();

        this.state.Vehicle = VehicleCatalog.HondaCivic;
        var civic = await connection.Execute(StandardCommands.Vin);

        this.state.Vehicle = VehicleCatalog.Ram2500;
        var ram = await connection.Execute(StandardCommands.Vin);

        Assert.Equal(VehicleCatalog.HondaCivic.Vin, civic);
        Assert.Equal(VehicleCatalog.Ram2500.Vin, ram);
        Assert.NotEqual(civic, ram);
    }

    [Fact]
    public async Task ReportsTheSeededTroubleCodes()
    {
        this.state.Vehicle = VehicleCatalog.VolkswagenGolfTdi;

        await using var connection = await this.Connect();
        var response = await connection.SendRaw("03");

        // Mode 03 answers with the codes packed two bytes each; P0401 is 0x0401 on the wire.
        Assert.Contains("0401", response.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TwoClientsSeeTheSameVehicle()
    {
        this.state.Vehicle = VehicleCatalog.Bmw330i;
        this.state.Find(0x01, 0x0D)!.Number = 63;

        await using var first = await this.Connect();
        await using var second = await this.Connect();

        Assert.Equal(63, await first.Execute(StandardCommands.VehicleSpeed));
        Assert.Equal(63, await second.Execute(StandardCommands.VehicleSpeed));
        Assert.Equal(VehicleCatalog.Bmw330i.Vin, await second.Execute(StandardCommands.Vin));
    }

    /// <summary>
    /// Reads a supported-PID bitmask reply ("41 00 BE 3E B8 11") and answers whether a PID's bit is
    /// set. Bits run from the most significant bit of the first data byte, which is PID 01.
    /// </summary>
    static bool SupportsPid(string response, byte pid)
    {
        var hex = new string([.. response.Where(Uri.IsHexDigit)]);

        // Drop the "4100" echo; what is left is the four mask bytes.
        var data = Convert.FromHexString(hex[4..12]);
        var index = pid - 1;

        return (data[index / 8] & (0x80 >> (index % 8))) != 0;
    }
}
