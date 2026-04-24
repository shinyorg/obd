using Microsoft.Extensions.DependencyInjection;
using Shiny.Obd.Ble;

namespace Shiny;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers BLE OBD services including device scanner and configuration.
    /// </summary>
    public static IServiceCollection AddShinyObdBluetoothLE(
        this IServiceCollection services,
        BleObdConfiguration? configuration = null
    )

    {
#if IOS || ANDROID
        services.AddBluetoothLE();
        services.AddSingleton(configuration ?? new BleObdConfiguration());
        services.AddSingleton<Obd.IObdDeviceScanner, BleObdDeviceScanner>();
#endif
        return services;
    }
}

