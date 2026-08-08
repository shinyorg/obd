using Microsoft.Extensions.Logging;
using Sample.Maui.Emulator;
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

        // ---- Client side: talk to a real adapter (Scan tab) ------------------------------------
        builder.Services.AddShinyObdBluetoothLE(new BleObdConfiguration
        {
            ServiceUuid = "FFF0",
            ReadCharacteristicUuid = "FFF1",
            WriteCharacteristicUuid = "FFF2"
        });

        // ---- Adapter side: pretend to be one (Adapter / Values / Faults tabs) -------------------
        // The emulator answers the same UUIDs the client above scans for, so two devices running this
        // app can drive each other with no configuration.
        builder.Services.AddBluetoothLeHosting();
        builder.Services.AddMdns();

        builder.Services.AddSingleton<ObdHostConfiguration>();
        builder.Services.AddSingleton<ObdEmulatorState>();
        builder.Services.AddSingleton<Elm327Responder>();
        builder.Services.AddSingleton<BleObdPeripheral>();
        builder.Services.AddSingleton<TcpObdServer>();
        builder.Services.AddSingleton<ObdHostService>();

        // Drives the emulator's live values from a scenario. Singleton so a drive keeps playing while
        // you are on another tab - which is the whole point of the long scenarios.
        builder.Services.AddSingleton<DrivingScenarioPlayer>();

        return builder.Build();
    }
}
