using Microsoft.Extensions.DependencyInjection;
using Shiny.BluetoothLE;
using Shiny.Obd;
using Shiny.Obd.Ble;

namespace Shiny.Obd.Tests;

/// <summary>
/// Covers <c>AddShinyObdBluetoothLE</c> on the plain <c>net10.0</c> target. Outside a browser the
/// Shiny.BluetoothLE <c>AddBluetoothLE()</c> call it makes registers nothing, which is exactly the Linux
/// situation (the BlueZ manager comes from <c>AddLinuxBluetoothLE()</c> in a separate package).
/// </summary>
public class BleRegistrationTests
{
    /// <summary>
    /// Stands in for whatever <c>services.AddBluetoothLE()</c> would have registered. The real
    /// managers need BlueZ or a browser, neither of which exists in a test run, and the registration
    /// wiring is what is under test rather than the radio.
    /// </summary>
    class FakeBleManager : IBleManager
    {
        public AccessState CurrentAccess => AccessState.Available;
        public bool IsScanning => false;
        public IObservable<AccessState> RequestAccess() => throw new NotImplementedException();
        public IPeripheral? GetKnownPeripheral(string peripheralUuid) => null;
        public void StopScan() { }
        public IEnumerable<IPeripheral> GetConnectedPeripherals() => [];
        public IObservable<ScanResult> Scan(ScanConfig? scanConfig = null) => throw new NotImplementedException();
    }

    static ServiceCollection WithBleManager()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBleManager, FakeBleManager>();
        return services;
    }

    [Fact]
    public void RegistersTheFullObdStack()
    {
        // The regression this locks down: the registration used to be wrapped in #if IOS || ANDROID
        // and returned silently having registered nothing on every other target. A Linux or Blazor
        // consumer got a no-op followed by an unrelated DI failure much later.
        var services = WithBleManager();
        services.AddShinyObdBluetoothLE();

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<BleObdConfiguration>());
        Assert.NotNull(provider.GetService<IObdDeviceScanner>());
        Assert.NotNull(provider.GetService<IObdTransport>());
        Assert.NotNull(provider.GetService<IObdConnection>());
    }

    [Fact]
    public void TransportResolvesToTheBleOne()
    {
        var services = WithBleManager();
        services.AddShinyObdBluetoothLE();

        using var provider = services.BuildServiceProvider();

        // BleObdTransport has three two-parameter constructors; the registration names the
        // IBleManager one explicitly so container heuristics cannot pick a different one.
        Assert.IsType<BleObdTransport>(provider.GetRequiredService<IObdTransport>());
    }

    [Fact]
    public void RegistrationOrderDoesNotMatter()
    {
        // A Linux or Blazor app calls AddBluetoothLE() from its platform package, and there is no
        // reason that has to come first. DI resolves lazily, so it must not.
        var services = new ServiceCollection();
        services.AddShinyObdBluetoothLE();
        services.AddSingleton<IBleManager, FakeBleManager>();

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IObdTransport>());
    }

    [Fact]
    public void TransportAndConnectionAreSingletons()
    {
        // An OBD adapter is one physical resource. Two connections would fight over one link.
        var services = WithBleManager();
        services.AddShinyObdBluetoothLE();

        using var provider = services.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<IObdTransport>(), provider.GetRequiredService<IObdTransport>());
        Assert.Same(provider.GetRequiredService<IObdConnection>(), provider.GetRequiredService<IObdConnection>());
    }

    [Fact]
    public void SuppliedConfigurationIsUsed()
    {
        var config = new BleObdConfiguration { DeviceNameFilter = "OBDCheck", ServiceUuid = "FFE0" };

        var services = WithBleManager();
        services.AddShinyObdBluetoothLE(config);

        using var provider = services.BuildServiceProvider();

        Assert.Same(config, provider.GetRequiredService<BleObdConfiguration>());
    }

    [Fact]
    public void MissingBleManager_ExplainsWhichCallIsMissing()
    {
        // The failure a Linux consumer hits without AddLinuxBluetoothLE(). The container's own
        // message names IBleManager and sends them hunting for a missing Shiny.Obd registration.
        var services = new ServiceCollection();
        services.AddShinyObdBluetoothLE();

        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<ObdException>(() => provider.GetRequiredService<IObdTransport>());

        Assert.Contains("Shiny.Obd.Ble.Linux", ex.Message, StringComparison.Ordinal);
        Assert.Contains("AddShinyObdLinuxBluetoothLE", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingBleManager_ScannerGetsTheSameExplanation()
    {
        // Regression for GitHub issue #1: the scanner was registered by type, so resolving it — usually
        // the first thing an app does — produced the container's generic "Unable to resolve service
        // for type IBleManager while attempting to activate BleObdDeviceScanner" instead of ours.
        var services = new ServiceCollection();
        services.AddShinyObdBluetoothLE();

        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<ObdException>(() => provider.GetRequiredService<IObdDeviceScanner>());
        Assert.Contains("AddShinyObdLinuxBluetoothLE", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScannerResolvesToTheBleOne()
    {
        var services = WithBleManager();
        services.AddShinyObdBluetoothLE();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<BleObdDeviceScanner>(provider.GetRequiredService<IObdDeviceScanner>());
    }

    [Fact]
    public void AnExplicitAddBluetoothLE_IsHarmless()
    {
        // Apps written against the old guidance call AddBluetoothLE() themselves as well. It is
        // idempotent, so doing so — before or after — must not duplicate or break anything.
        var services = WithBleManager();
        services.AddBluetoothLE();
        services.AddShinyObdBluetoothLE();
        services.AddBluetoothLE();

        using var provider = services.BuildServiceProvider();

        Assert.Single(provider.GetServices<IBleManager>());
        Assert.NotNull(provider.GetService<IObdDeviceScanner>());
    }

    [Fact]
    public void CallingTwice_DoesNotDuplicateRegistrations()
    {
        var services = WithBleManager();
        services.AddShinyObdBluetoothLE();
        services.AddShinyObdBluetoothLE();

        using var provider = services.BuildServiceProvider();

        Assert.Single(provider.GetServices<IObdTransport>());
        Assert.Single(provider.GetServices<IObdDeviceScanner>());
    }

    [Fact]
    public void AnAlreadyRegisteredTransportIsNotReplaced()
    {
        // TryAdd throughout, so an app that registered its own transport — or called the serial
        // registration first — keeps it instead of having it silently swapped for a BLE one.
        var services = WithBleManager();
        services.AddSingleton<IObdTransport>(new StubTransport());
        services.AddShinyObdBluetoothLE();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<StubTransport>(provider.GetRequiredService<IObdTransport>());
    }

    class StubTransport : IObdTransport
    {
        public bool IsConnected => false;
        public Task Connect(CancellationToken ct = default) => Task.CompletedTask;
        public Task Disconnect() => Task.CompletedTask;
        public Task<string> Send(string command, CancellationToken ct = default) => Task.FromResult("OK");
        public ValueTask DisposeAsync() => default;
    }
}
