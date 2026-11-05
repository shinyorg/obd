using Microsoft.Extensions.DependencyInjection;
using Shiny.BluetoothLE;
using Shiny.Obd.Ble;

namespace Shiny.Obd.Tests;

/// <summary>
/// Covers <c>AddShinyObdLinuxBluetoothLE</c> from Shiny.Obd.Ble.Linux. Assertions stay at the
/// descriptor level: resolving the BlueZ manager needs a D-Bus system bus, which a test run (or a Mac)
/// does not have.
/// </summary>
public class LinuxBleRegistrationTests
{
    [Fact]
    public void RegistersTheBlueZManagerAndTheObdStack()
    {
        var services = new ServiceCollection();
        services.AddShinyObdLinuxBluetoothLE();

        Assert.Contains(services, x => x.ServiceType == typeof(IBleManager));
        Assert.Contains(services, x => x.ServiceType == typeof(IObdDeviceScanner));
        Assert.Contains(services, x => x.ServiceType == typeof(IObdTransport));
        Assert.Contains(services, x => x.ServiceType == typeof(IObdConnection));
        Assert.Contains(services, x => x.ServiceType == typeof(BleObdConfiguration));
    }

    [Fact]
    public void SuppliedConfigurationIsUsed()
    {
        var config = new BleObdConfiguration { DeviceNameFilter = "OBDCheck" };

        var services = new ServiceCollection();
        services.AddShinyObdLinuxBluetoothLE(config);

        using var provider = services.BuildServiceProvider();
        Assert.Same(config, provider.GetRequiredService<BleObdConfiguration>());
    }

    [Fact]
    public void CombiningWithTheOtherRegistrations_DoesNotDuplicate()
    {
        // Apps following the older two-call guidance (AddLinuxBluetoothLE + AddShinyObdBluetoothLE)
        // may add this on top. Every piece skips what is already registered.
        var services = new ServiceCollection();
        services.AddLinuxBluetoothLE();
        services.AddShinyObdBluetoothLE();
        services.AddShinyObdLinuxBluetoothLE();
        services.AddShinyObdLinuxBluetoothLE();

        Assert.Single(services, x => x.ServiceType == typeof(IBleManager));
        Assert.Single(services, x => x.ServiceType == typeof(IObdDeviceScanner));
        Assert.Single(services, x => x.ServiceType == typeof(IObdTransport));
    }
}
