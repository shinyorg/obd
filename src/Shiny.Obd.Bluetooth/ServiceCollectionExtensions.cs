using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shiny.Bluetooth;
using Shiny.Obd;
using Shiny.Obd.Bluetooth;

namespace Shiny;

public static class BluetoothObdServiceCollectionExtensions
{
    /// <summary>
    /// Registers the classic Bluetooth (RFCOMM/SPP) OBD transport, device scanner and configuration.
    /// </summary>
    /// <remarks>
    /// Works on Android, Windows, macOS and Linux - every platform Shiny.Bluetooth supports. Not on iOS
    /// or Mac Catalyst; use <c>AddShinyObdBluetoothLE()</c> from Shiny.Obd.Ble there.
    ///
    /// <para>
    /// On Android the Bluetooth manager is registered for you. <b>Everywhere else call
    /// <c>services.AddBluetooth()</c> yourself</b> (from <c>Shiny.Bluetooth</c> on a Windows or macOS
    /// head), or <c>services.AddLinuxBluetooth()</c> from <c>Shiny.Bluetooth.Linux</c> on Linux - the
    /// same split as the BLE transport, and for the same reason: only the app knows which platform
    /// assembly it is running.
    /// </para>
    ///
    /// <para>
    /// Registers <see cref="IObdTransport"/> and <see cref="IObdConnection"/> as singletons - an OBD
    /// adapter is a single physical resource. Everything is registered with <c>TryAdd</c>, so calling
    /// this alongside another <c>AddShinyObd*</c> leaves whichever ran first in place rather than
    /// silently swapping the transport out.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddShinyObdBluetooth(
        this IServiceCollection services,
        Action<BluetoothObdConfiguration>? configure = null
    )
    {
#if ANDROID
        services.AddBluetooth();
#endif
        var config = new BluetoothObdConfiguration();
        configure?.Invoke(config);

        services.TryAddSingleton(config);
        services.TryAddSingleton<IObdDeviceScanner>(sp => new BluetoothObdDeviceScanner(
            GetManager(sp),
            sp.GetRequiredService<BluetoothObdConfiguration>()
        ));

        // Built by factory: the transport has several constructors and naming the one we want keeps
        // the container from choosing based on whatever else happens to be registered.
        services.TryAddSingleton<IObdTransport>(sp => new BluetoothObdTransport(
            GetManager(sp),
            sp.GetRequiredService<BluetoothObdConfiguration>()
        ));
        services.TryAddSingleton<IObdConnection>(sp => new ObdConnection(sp.GetRequiredService<IObdTransport>()));

        return services;
    }

    /// <summary>
    /// Registers the classic Bluetooth OBD transport against a known adapter address.
    /// </summary>
    public static IServiceCollection AddShinyObdBluetooth(this IServiceCollection services, string address)
        => services.AddShinyObdBluetooth(x => x.Address = address);

    /// <summary>
    /// Resolves the Bluetooth manager, replacing the container's generic "unable to resolve service"
    /// with the instruction that actually fixes it.
    /// </summary>
    static IBluetoothManager GetManager(IServiceProvider sp)
        => sp.GetService<IBluetoothManager>() ?? throw new ObdException(
            "No IBluetoothManager is registered. AddShinyObdBluetooth() registers the OBD pieces but " +
            "can only register the classic Bluetooth manager itself on Android - call " +
            "services.AddBluetooth() (Shiny.Bluetooth) on Windows/macOS or services.AddLinuxBluetooth() " +
            "(Shiny.Bluetooth.Linux) on Linux. Classic Bluetooth is not available on iOS or Mac " +
            "Catalyst; use AddShinyObdBluetoothLE() there."
        );
}
