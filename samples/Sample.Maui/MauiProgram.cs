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

        builder.Services.AddShinyObdBluetoothLE(new BleObdConfiguration
        {
            ServiceUuid = "FFF0",
            ReadCharacteristicUuid = "FFF1",
            WriteCharacteristicUuid = "FFF2"
        });

        return builder.Build();
    }
}
