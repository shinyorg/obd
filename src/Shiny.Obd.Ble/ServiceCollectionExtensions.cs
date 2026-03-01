#if IOS || ANDROID
using Microsoft.Extensions.DependencyInjection;
using Shiny.Obd.Ble;

namespace Shiny;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers BLE OBD services including device scanner and configuration.
    /// You must also call <c>services.AddBluetoothLE()</c> from your platform host (MAUI, etc.).
    /// </summary>
    public static IServiceCollection AddShinyObdBluetoothLE(
        this IServiceCollection services,
        BleObdConfiguration? configuration = null
    )
    {
        services.AddSingleton(configuration ?? new BleObdConfiguration());
        services.AddSingleton<Obd.IObdDeviceScanner, BleObdDeviceScanner>();
        return services;
    }
}
#endif
