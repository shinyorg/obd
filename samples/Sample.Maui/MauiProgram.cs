using Microsoft.Extensions.Logging;
using Shiny.Obd.Emulator;
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
        // app can drive each other with no configuration. AddMdns is optional - it only adds the
        // _obd._tcp announcement, so a client can find the WiFi side without being told an address.
        builder.Services.AddMdns();
        builder.Services.AddObdEmulator();
        builder.Services.AddObdEmulatorBluetoothLE();

        return builder.Build();
    }
}
