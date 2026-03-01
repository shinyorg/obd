using Microsoft.Extensions.DependencyInjection;
using Shiny;
using Shiny.Obd;
using Shiny.Obd.Ble;

namespace Sample.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        var config = new BleObdConfiguration
        {
            ServiceUuid = "FFF0",
            ReadCharacteristicUuid = "FFF1",
            WriteCharacteristicUuid = "FFF2"
        };

        builder.Services.AddSingleton(config);
        builder.Services.AddBluetoothLE();
        builder.Services.AddSingleton<IObdDeviceScanner, BleObdDeviceScanner>();
        builder.Services.AddSingleton<ScanViewModel>();
        builder.Services.AddSingleton<DashboardViewModel>();
        builder.Services.AddTransient<ScanPage>();
        builder.Services.AddTransient<DashboardPage>();

        return builder.Build();
    }
}
