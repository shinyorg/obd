using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Shiny.Bluetooth;
using Shiny.Obd;
using Shiny.Obd.Bluetooth;
using Shiny.Obd.Commands;

namespace Shiny.Obd.Tests;

/// <summary>
/// A classic Bluetooth device that exists only as an address and a name. Connections never go through
/// it - <see cref="BluetoothConnection"/> cannot be constructed outside Shiny.Bluetooth - so the tests
/// swap <see cref="BluetoothObdTransport.OpenStream"/> for a loopback socket to a
/// <see cref="FakeElmAdapter"/> instead. An RFCOMM channel is a byte stream exactly like TCP, so that
/// is a faithful stand-in for everything past the connect.
/// </summary>
sealed class FakeBluetoothDevice(string address, string? name, bool paired = true) : IBluetoothDevice
{
    public string Address { get; } = address;
    public string? Name { get; } = name;
    public bool IsPaired { get; } = paired;
    public uint? ClassOfDevice => null;
    public BluetoothMajorDeviceClass? MajorDeviceClass => null;
    public Task<bool> Pair(CancellationToken ct = default) => Task.FromResult(true);
    public Task<BluetoothConnection> Connect(Guid serviceUuid, bool secure = true, CancellationToken ct = default)
        => throw new NotSupportedException("Tests connect through BluetoothObdTransport.OpenStream");
}

sealed class FakeBluetoothManager : IBluetoothManager
{
    public AccessState Access { get; set; } = AccessState.Available;
    public BluetoothCapabilities Capabilities { get; set; } = BluetoothCapabilities.Discovery | BluetoothCapabilities.Connect | BluetoothCapabilities.Pairing;
    public List<IBluetoothDevice> Paired { get; } = [];
    public List<IBluetoothDevice> Nearby { get; } = [];
    public List<string> AddressesLookedUp { get; } = [];
    public int Inquiries { get; private set; }

    public Task<AccessState> RequestAccess(CancellationToken ct = default) => Task.FromResult(this.Access);

    public Task<IReadOnlyList<IBluetoothDevice>> GetPairedDevices(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<IBluetoothDevice>>(this.Paired.ToList());

    public Task<IBluetoothDevice> GetDevice(string address, CancellationToken ct = default)
    {
        if (address.Length < 12)
            throw new ArgumentException("not an address", nameof(address));

        this.AddressesLookedUp.Add(address);
        return Task.FromResult<IBluetoothDevice>(
            this.Paired.FirstOrDefault(x => x.Address == address) ?? new FakeBluetoothDevice(address, null, false)
        );
    }

    public async IAsyncEnumerable<BluetoothDiscoveryResult> Discover([EnumeratorCancellation] CancellationToken ct = default)
    {
        this.Inquiries++;
        foreach (var device in this.Nearby)
            yield return new BluetoothDiscoveryResult(device, -60);

        // An inquiry runs for a while even when it has nothing more to report.
        await Task.Delay(50, ct);
    }

    public Task<BluetoothListener> Listen(Guid serviceUuid, string serviceName, bool secure = true, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<bool> RequestDiscoverable(TimeSpan duration, CancellationToken ct = default)
        => throw new NotSupportedException();
}

static class BluetoothTestHelpers
{
    public static BluetoothObdConfiguration Fast(string? address = null) => new()
    {
        Address = address,
        CommandTimeout = TimeSpan.FromSeconds(2),
        ConnectTimeout = TimeSpan.FromSeconds(2),
        ConnectRetryDelay = TimeSpan.Zero
    };

    /// <summary>A transport whose RFCOMM channel is a loopback socket to <paramref name="adapter"/>.</summary>
    public static BluetoothObdTransport Over(FakeElmAdapter adapter, FakeBluetoothManager manager, BluetoothObdConfiguration config)
    {
        var transport = new BluetoothObdTransport(manager, config);
        transport.OpenStream = (_, _, ct) => Loopback(adapter, ct);
        return transport;
    }

    public static async Task<Stream> Loopback(FakeElmAdapter adapter, CancellationToken ct)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(IPAddress.Loopback, adapter.Port, ct);
        return new NetworkStream(socket, ownsSocket: true);
    }
}

public class BluetoothObdConfigurationTests
{
    [Fact]
    public void Defaults_UseSerialPortProfileSecurely()
    {
        var config = new BluetoothObdConfiguration();

        Assert.Equal(BluetoothServiceUuids.SerialPort, config.ServiceUuid);
        Assert.True(config.Secure);
        Assert.Null(config.Address);
        Assert.True(config.ConnectAttempts >= 2);
    }

    [Theory]
    [InlineData("OBDII", true)]
    [InlineData("OBD2", true)]
    [InlineData("OBDLink MX+ 12345", true)]
    [InlineData("V-LINK", true)]
    [InlineData("Vgate iCar Pro", true)]
    [InlineData("veepeak", true)]
    [InlineData("AirPods Pro", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void Matches_TheCommonAdapterNames(string? name, bool expected)
        => Assert.Equal(expected, new BluetoothObdConfiguration().Matches(name));

    [Fact]
    public void Matches_EverythingWhenFiltersAreEmpty()
    {
        var config = new BluetoothObdConfiguration { DeviceNameFilters = [] };

        Assert.True(config.Matches("AirPods Pro"));
        Assert.True(config.Matches(null));
    }
}

public class BluetoothObdTransportTests
{
    const string AdapterAddress = "00:1D:A5:68:98:8B";

    [Fact]
    public async Task Send_ThrowsWhenNotConnected()
    {
        var transport = new BluetoothObdTransport(new FakeBluetoothManager(), AdapterAddress);

        Assert.False(transport.IsConnected);
        await Assert.ThrowsAsync<ObdException>(() => transport.Send("010D\r"));
    }

    [Fact]
    public async Task Connect_ByAddress_AndSend_StripsThePrompt()
    {
        await using var adapter = FakeElmAdapter.Always("41 0D 32");
        var manager = new FakeBluetoothManager();
        await using var transport = BluetoothTestHelpers.Over(adapter, manager, BluetoothTestHelpers.Fast(AdapterAddress));

        await transport.Connect();

        Assert.True(transport.IsConnected);
        Assert.Equal(AdapterAddress, transport.ConnectedDevice?.Address);
        Assert.Equal([AdapterAddress], manager.AddressesLookedUp);
        Assert.Equal("41 0D 32", await transport.Send("010D\r"));
    }

    [Fact]
    public async Task Connect_WithoutAddress_PicksTheFirstPairedAdapter()
    {
        // Headphones are paired on every phone; they must not be mistaken for the adapter just
        // because they come first in the list.
        await using var adapter = FakeElmAdapter.Always("OK");
        var manager = new FakeBluetoothManager();
        manager.Paired.Add(new FakeBluetoothDevice("11:11:11:11:11:11", "AirPods Pro"));
        manager.Paired.Add(new FakeBluetoothDevice(AdapterAddress, "OBDII"));

        IBluetoothDevice? opened = null;
        await using var transport = new BluetoothObdTransport(manager, BluetoothTestHelpers.Fast());
        transport.OpenStream = (device, _, ct) =>
        {
            opened = device;
            return BluetoothTestHelpers.Loopback(adapter, ct);
        };

        await transport.Connect();

        Assert.Equal(AdapterAddress, opened?.Address);
        Assert.Empty(manager.AddressesLookedUp);
    }

    [Fact]
    public async Task Connect_WhenNothingPairedMatches_NamesWhatIsPaired()
    {
        var manager = new FakeBluetoothManager();
        manager.Paired.Add(new FakeBluetoothDevice("11:11:11:11:11:11", "AirPods Pro"));

        await using var transport = new BluetoothObdTransport(manager, BluetoothTestHelpers.Fast());

        var ex = await Assert.ThrowsAsync<ObdException>(() => transport.Connect());
        Assert.Contains("AirPods Pro", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Address", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connect_WhenNothingIsPaired_SaysToPair()
    {
        await using var transport = new BluetoothObdTransport(new FakeBluetoothManager(), BluetoothTestHelpers.Fast());

        var ex = await Assert.ThrowsAsync<ObdException>(() => transport.Connect());
        Assert.Contains("Pair", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connect_WithAnInvalidAddress_ExplainsTheFormat()
    {
        await using var transport = new BluetoothObdTransport(new FakeBluetoothManager(), "nope");

        var ex = await Assert.ThrowsAsync<ObdException>(() => transport.Connect());
        Assert.Contains("AA:BB:CC:DD:EE:FF", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AccessState.Disabled, "turned off")]
    [InlineData(AccessState.NotSupported, "Shiny.Obd.Ble")]
    [InlineData(AccessState.Denied, "not granted")]
    public async Task Connect_WithoutAccess_SaysWhy(AccessState access, string expected)
    {
        var manager = new FakeBluetoothManager { Access = access };
        await using var transport = new BluetoothObdTransport(manager, AdapterAddress);

        var ex = await Assert.ThrowsAsync<ObdException>(() => transport.Connect());
        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connect_RetriesAFailedFirstAttempt()
    {
        // The first connect after power-up failing outright is routine on Android.
        await using var adapter = FakeElmAdapter.Always("OK");
        var attempts = 0;

        await using var transport = new BluetoothObdTransport(new FakeBluetoothManager(), BluetoothTestHelpers.Fast(AdapterAddress));
        transport.OpenStream = (_, _, ct) =>
        {
            if (++attempts == 1)
                throw new BluetoothConnectionException("read failed, socket might closed");

            return BluetoothTestHelpers.Loopback(adapter, ct);
        };

        await transport.Connect();

        Assert.Equal(2, attempts);
        Assert.True(transport.IsConnected);
    }

    [Fact]
    public async Task Connect_WhenEveryAttemptFails_WrapsTheLastError()
    {
        var attempts = 0;
        var config = BluetoothTestHelpers.Fast(AdapterAddress);
        config.ConnectAttempts = 3;

        await using var transport = new BluetoothObdTransport(new FakeBluetoothManager(), config);
        transport.OpenStream = (_, _, _) =>
        {
            attempts++;
            throw new BluetoothConnectionException("host is down");
        };

        var ex = await Assert.ThrowsAsync<ObdException>(() => transport.Connect());

        Assert.Equal(3, attempts);
        Assert.IsType<BluetoothConnectionException>(ex.InnerException);
        Assert.Contains(AdapterAddress, ex.Message, StringComparison.Ordinal);
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task Connect_DoesNotRetryAMissingPermission()
    {
        var attempts = 0;
        await using var transport = new BluetoothObdTransport(new FakeBluetoothManager(), BluetoothTestHelpers.Fast(AdapterAddress));
        transport.OpenStream = (_, _, _) =>
        {
            attempts++;
            throw new BluetoothPermissionException("BLUETOOTH_CONNECT is not granted");
        };

        var ex = await Assert.ThrowsAsync<ObdException>(() => transport.Connect());

        Assert.Equal(1, attempts);
        Assert.Contains("BLUETOOTH_CONNECT", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connect_PassesTheConfiguredServiceAndSecurity()
    {
        await using var adapter = FakeElmAdapter.Always("OK");
        var config = BluetoothTestHelpers.Fast(AdapterAddress);
        config.Secure = false;

        BluetoothObdConfiguration? used = null;
        await using var transport = new BluetoothObdTransport(new FakeBluetoothManager(), config);
        transport.OpenStream = (_, cfg, ct) =>
        {
            used = cfg;
            return BluetoothTestHelpers.Loopback(adapter, ct);
        };

        await transport.Connect();

        Assert.False(used?.Secure);
        Assert.Equal(BluetoothServiceUuids.SerialPort, used?.ServiceUuid);
    }

    [Fact]
    public async Task Send_WhenTheAdapterGoesQuiet_ThrowsObdTimeout()
    {
        await using var adapter = FakeElmAdapter.Silent();
        await using var transport = BluetoothTestHelpers.Over(adapter, new FakeBluetoothManager(), BluetoothTestHelpers.Fast(AdapterAddress));

        await transport.Connect();

        var ex = await Assert.ThrowsAsync<ObdTimeoutException>(() => transport.Send("010D\r"));
        Assert.Equal("010D", ex.Command);
    }

    [Fact]
    public async Task Send_AfterATimeout_IsNotAnsweredByTheLateReply()
    {
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

        var config = BluetoothTestHelpers.Fast(AdapterAddress);
        config.CommandTimeout = TimeSpan.FromMilliseconds(300);

        await using var transport = BluetoothTestHelpers.Over(adapter, new FakeBluetoothManager(), config);
        await transport.Connect();

        await Assert.ThrowsAsync<ObdTimeoutException>(() => transport.Send("SLOW\r"));

        gate.SetResult();
        await Task.Delay(100);

        Assert.Equal("41 0D 32", await transport.Send("FAST\r"));
    }

    [Fact]
    public async Task Send_WhenTheAdapterDropsTheLink_FailsImmediately()
    {
        // The ignition going off mid-session: the adapter loses power and the RFCOMM channel with it.
        await using var adapter = FakeElmAdapter.Always("OK");
        await using var transport = BluetoothTestHelpers.Over(adapter, new FakeBluetoothManager(), BluetoothTestHelpers.Fast(AdapterAddress));

        await transport.Connect();
        adapter.DropOnNextCommand = true;

        var ex = await Assert.ThrowsAsync<ObdException>(() => transport.Send("010D\r"));

        Assert.IsNotType<ObdTimeoutException>(ex);
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task Connect_AfterTheAdapterDroppedTheLink_Reconnects()
    {
        await using var adapter = FakeElmAdapter.Always("41 0D 32");
        await using var transport = BluetoothTestHelpers.Over(adapter, new FakeBluetoothManager(), BluetoothTestHelpers.Fast(AdapterAddress));

        await transport.Connect();
        adapter.DropOnNextCommand = true;
        await Assert.ThrowsAsync<ObdException>(() => transport.Send("010D\r"));
        adapter.DropOnNextCommand = false;

        await transport.Connect();
        Assert.Equal("41 0D 32", await transport.Send("010D\r"));
    }

    [Fact]
    public async Task Disconnect_LeavesTheTransportReusable()
    {
        await using var adapter = FakeElmAdapter.Always("41 0D 32");
        await using var transport = BluetoothTestHelpers.Over(adapter, new FakeBluetoothManager(), BluetoothTestHelpers.Fast(AdapterAddress));

        await transport.Connect();
        await transport.Disconnect();

        Assert.False(transport.IsConnected);
        Assert.Null(transport.ConnectedDevice);

        await transport.Connect();
        Assert.Equal("41 0D 32", await transport.Send("010D\r"));
    }

    [Fact]
    public async Task DisposeAsync_IsSafeBeforeConnect()
    {
        var transport = new BluetoothObdTransport(new FakeBluetoothManager(), AdapterAddress);
        await transport.DisposeAsync();
    }

    [Fact]
    public void Construction_FromForeignDiscoveredDevice_ExplainsTheMismatch()
    {
        var device = new ObdDiscoveredDevice("Some WiFi thing", "abc", new object());

        var ex = Assert.Throws<ObdException>(() => new BluetoothObdTransport(new FakeBluetoothManager(), device, new BluetoothObdConfiguration()));
        Assert.Contains("BluetoothObdDeviceScanner", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Construction_FromDiscoveredDevice_ConnectsToThatDevice()
    {
        await using var adapter = FakeElmAdapter.Always("OK");
        var manager = new FakeBluetoothManager();
        var native = new FakeBluetoothDevice(AdapterAddress, "OBDII", paired: false);

        IBluetoothDevice? opened = null;
        await using var transport = new BluetoothObdTransport(manager, new ObdDiscoveredDevice("OBDII", AdapterAddress, native), BluetoothTestHelpers.Fast());
        transport.OpenStream = (device, _, ct) =>
        {
            opened = device;
            return BluetoothTestHelpers.Loopback(adapter, ct);
        };

        await transport.Connect();

        Assert.Same(native, opened);
        Assert.Empty(manager.AddressesLookedUp);
    }

    [Fact]
    public async Task FullStack_DetectsTheAdapterAndParsesAReading()
    {
        await using var adapter = FakeElmAdapter.Responding(command => Task.FromResult<string?>(command switch
        {
            "ATI" => "ELM327 v1.5",
            "010D" => "41 0D 32",
            _ => "OK"
        }));

        await using var transport = BluetoothTestHelpers.Over(adapter, new FakeBluetoothManager(), BluetoothTestHelpers.Fast(AdapterAddress));
        await using var connection = new ObdConnection(transport);
        await connection.Connect();

        Assert.Equal(ObdAdapterType.Elm327, connection.DetectedAdapter?.Type);
        Assert.Equal(50, await connection.Execute(StandardCommands.VehicleSpeed));
    }
}

public class BluetoothObdDeviceScannerTests
{
    static async Task<List<ObdDiscoveredDevice>> ScanFor(FakeBluetoothManager manager, BluetoothObdConfiguration config, TimeSpan duration)
    {
        var found = new List<ObdDiscoveredDevice>();
        using var cts = new CancellationTokenSource(duration);
        await new BluetoothObdDeviceScanner(manager, config).Scan(d => { lock (found) found.Add(d); }, cts.Token);
        return found;
    }

    [Fact]
    public async Task Scan_ReportsMatchingPairedThenDiscoveredDevices_Once()
    {
        var manager = new FakeBluetoothManager();
        manager.Paired.Add(new FakeBluetoothDevice("11:11:11:11:11:11", "AirPods Pro"));
        manager.Paired.Add(new FakeBluetoothDevice("22:22:22:22:22:22", "OBDII"));
        manager.Nearby.Add(new FakeBluetoothDevice("22:22:22:22:22:22", "OBDII"));
        manager.Nearby.Add(new FakeBluetoothDevice("33:33:33:33:33:33", "OBDLink MX+", paired: false));

        var found = await ScanFor(manager, new BluetoothObdConfiguration(), TimeSpan.FromMilliseconds(400));

        Assert.Equal(["22:22:22:22:22:22", "33:33:33:33:33:33"], found.Select(x => x.Id));
        Assert.IsAssignableFrom<IBluetoothDevice>(found[0].NativeDevice);
        Assert.True(manager.Inquiries >= 2, "inquiries should repeat until cancelled");
    }

    [Fact]
    public async Task Scan_WithDiscoveryOff_ListsPairedAndWaitsForCancel()
    {
        var manager = new FakeBluetoothManager();
        manager.Paired.Add(new FakeBluetoothDevice("22:22:22:22:22:22", "OBDII"));
        manager.Nearby.Add(new FakeBluetoothDevice("33:33:33:33:33:33", "OBDLink MX+", paired: false));

        var started = Environment.TickCount64;
        var found = await ScanFor(manager, new BluetoothObdConfiguration { DiscoverUnpaired = false }, TimeSpan.FromMilliseconds(300));

        Assert.Single(found);
        Assert.Equal(0, manager.Inquiries);
        Assert.True(Environment.TickCount64 - started >= 250, "scan should run until cancelled");
    }

    [Fact]
    public async Task Scan_WithoutAccess_Throws()
    {
        var manager = new FakeBluetoothManager { Access = AccessState.Disabled };

        await Assert.ThrowsAsync<ObdException>(() => ScanFor(manager, new BluetoothObdConfiguration(), TimeSpan.FromSeconds(1)));
    }
}

public class BluetoothRegistrationTests
{
    [Fact]
    public void RegistersTheFullObdStack()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBluetoothManager, FakeBluetoothManager>();
        services.AddShinyObdBluetooth("00:1D:A5:68:98:8B");

        using var provider = services.BuildServiceProvider();

        Assert.Equal("00:1D:A5:68:98:8B", provider.GetRequiredService<BluetoothObdConfiguration>().Address);
        Assert.IsType<BluetoothObdDeviceScanner>(provider.GetRequiredService<IObdDeviceScanner>());
        Assert.IsType<BluetoothObdTransport>(provider.GetRequiredService<IObdTransport>());
        Assert.NotNull(provider.GetService<IObdConnection>());
    }

    [Fact]
    public void WithoutAManager_SaysWhichRegistrationIsMissing()
    {
        var services = new ServiceCollection();
        services.AddShinyObdBluetooth();

        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<ObdException>(() => provider.GetRequiredService<IObdTransport>());
        Assert.Contains("AddBluetooth()", ex.Message, StringComparison.Ordinal);
        Assert.Contains("AddLinuxBluetooth()", ex.Message, StringComparison.Ordinal);
    }
}
