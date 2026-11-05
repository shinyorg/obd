using Microsoft.Extensions.DependencyInjection;
using Shiny.Obd.Ble;

namespace Shiny;

public static class LinuxObdBleServiceCollectionExtensions
{
    /// <summary>
    /// Registers the BlueZ BLE manager (Shiny.BluetoothLE.Linux) and the BLE OBD transport, device
    /// scanner, connection and configuration — the Linux equivalent of
    /// <see cref="ServiceCollectionExtensions.AddShinyObdBluetoothLE"/>, in one call.
    /// </summary>
    /// <remarks>
    /// A separate method name rather than an overload: this package references Shiny.Obd.Ble, so an
    /// <c>AddShinyObdBluetoothLE</c> with the same signature here would make every call ambiguous.
    /// Calling <c>AddLinuxBluetoothLE()</c> or <c>AddShinyObdBluetoothLE()</c> yourself as well is
    /// harmless — both skip what is already registered.
    /// </remarks>
    public static IServiceCollection AddShinyObdLinuxBluetoothLE(
        this IServiceCollection services,
        BleObdConfiguration? configuration = null
    )
    {
        services.AddLinuxBluetoothLE();
        services.AddShinyObdBluetoothLE(configuration);
        return services;
    }
}
