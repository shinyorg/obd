using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shiny.Obd.Emulator;
using Shiny.Obd.Emulator.Ble;

namespace Shiny;

public static class BleServiceCollectionExtensions
{
    /// <summary>
    /// Adds a Bluetooth LE front-end to the emulator, so it advertises as a BLE OBD-II adapter and a
    /// client scanning for one finds it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call <c>AddObdEmulator()</c> as well — this only adds a transport, and the vehicle, responder
    /// and host all live in <c>Shiny.Obd.Emulator</c>. Registration order does not matter.
    /// </para>
    /// <para>
    /// On iOS and Android the BLE hosting manager is registered for you. On the other platforms
    /// Shiny.BluetoothLE.Hosting supports, call <c>services.AddBluetoothLeHosting()</c> yourself.
    /// </para>
    /// <para>
    /// The defaults advertise the FFF0/FFF1/FFF2 triple used by Veepeak OBDCheck BLE and the ELM327
    /// clones built on the same module — which is also what <c>BleObdConfiguration</c> defaults to,
    /// so a Shiny.Obd client finds this emulator with no configuration on either side.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddObdEmulatorBluetoothLE(this IServiceCollection services)
    {
#if IOS || ANDROID
        services.AddBluetoothLeHosting();
#endif
        services.TryAddSingleton<BleObdPeripheral>();
        services.AddSingleton<IObdEmulatorTransport>(sp => sp.GetRequiredService<BleObdPeripheral>());

        return services;
    }
}
