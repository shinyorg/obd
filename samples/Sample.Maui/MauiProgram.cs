using Microsoft.Extensions.Logging;
using Shiny;
using Shiny.Obd.Ble;

namespace Sample.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp
            .CreateBuilder()
            .UseMauiApp<App>()
            .UseShiny()
            .UseShinyShell(x => x.AddGeneratedMaps());

#if DEBUG
        // Debug level is what surfaces the per-advertisement dump from BleObdDeviceScanner - the way to
        // see what an adapter actually advertises when it isn't turning up in the scan list.
        builder.Logging.AddDebug().SetMinimumLevel(LogLevel.Debug);
#endif

        builder.Services.AddShinyObdBluetoothLE(new BleObdConfiguration
        {
            ServiceUuid = "FFF0",
            ReadCharacteristicUuid = "FFF1",
            WriteCharacteristicUuid = "FFF2"
        });

        return builder.Build();
    }
}
