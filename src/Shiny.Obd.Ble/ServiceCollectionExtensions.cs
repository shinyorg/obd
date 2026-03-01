using Microsoft.Extensions.DependencyInjection;
using Shiny.Obd.Ble;

namespace Shiny;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddShinyObdBluetoothLE(
        this IServiceCollection services,
        BleObdConfiguration? configuration = null
    )
    {
        services.AddBluetoothLE();
        services.AddSingleton(configuration ?? new BleObdConfiguration());
        services.AddSingleton<Obd.IObdDeviceScanner, BleObdDeviceScanner>();
        return services;
    }
}
