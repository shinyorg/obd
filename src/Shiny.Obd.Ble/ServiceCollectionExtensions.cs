using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shiny.BluetoothLE;
using Shiny.Obd;
using Shiny.Obd.Ble;

namespace Shiny;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the BLE OBD transport, device scanner and configuration.
    /// </summary>
    /// <remarks>
    /// Works on every platform Shiny.BluetoothLE supports — iOS, Android, Mac Catalyst, macOS,
    /// Windows, Linux (BlueZ) and Blazor WebAssembly (Web Bluetooth).
    ///
    /// <para>
    /// On iOS and Android the BLE manager is registered for you. **Everywhere else you must call
    /// <c>services.AddBluetoothLE()</c> yourself**, from whichever platform package you installed:
    /// <c>Shiny.BluetoothLE.Linux</c>, <c>Shiny.BluetoothLE.Blazor</c>, or <c>Shiny.BluetoothLE</c>
    /// on a Windows/Apple head. This library cannot do it for you there, and the reason is a hard
    /// constraint rather than an omission: Linux and Blazor both ship a <c>net10.0</c> assembly and
    /// both declare <c>Shiny.AddBluetoothLE(IServiceCollection)</c>, so a package referencing both
    /// would make every call to it ambiguous (CS0121). Only the app knows which one it is running on.
    /// </para>
    ///
    /// <para>
    /// Registration order does not matter — DI resolves lazily, so
    /// <c>AddBluetoothLE()</c> may come before or after this call.
    /// </para>
    ///
    /// <para>
    /// Registers <see cref="IObdTransport"/> and <see cref="IObdConnection"/> as singletons; an OBD
    /// adapter is a single physical resource and a scoped registration would leave two consumers
    /// fighting over one link. Everything is registered with <c>TryAdd</c>, so calling both this and
    /// <c>AddShinyObdSerial()</c> leaves whichever ran first in place rather than silently swapping
    /// the transport out. If you want a fallback chain across transports, construct them yourself
    /// instead of registering both.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddShinyObdBluetoothLE(
        this IServiceCollection services,
        BleObdConfiguration? configuration = null
    )
    {
#if IOS || ANDROID
        // Unambiguous here: the only AddBluetoothLE in scope is the one from Shiny.BluetoothLE's
        // own platform target, which this package already references.
        services.AddBluetoothLE();
#endif
        services.TryAddSingleton(configuration ?? new BleObdConfiguration());
        services.TryAddSingleton<IObdDeviceScanner, BleObdDeviceScanner>();

        // Built by factory rather than by type. BleObdTransport has three two-parameter constructors
        // (IBleManager, IPeripheral, ObdDiscoveredDevice) and letting the container choose between
        // them depends on what else happens to be registered — naming the one we want makes it
        // deterministic. This is the scan-for-an-adapter constructor.
        services.TryAddSingleton<IObdTransport>(sp => new BleObdTransport(
            GetBleManager(sp),
            sp.GetRequiredService<BleObdConfiguration>()
        ));

        services.TryAddSingleton<IObdConnection>(sp => new ObdConnection(
            sp.GetRequiredService<IObdTransport>()
        ));

        return services;
    }

    /// <summary>
    /// Resolves the BLE manager, replacing the container's generic "unable to resolve service"
    /// with the one instruction that actually fixes it.
    /// </summary>
    /// <remarks>
    /// This is the failure a Linux or Blazor consumer hits, and the default message sends them
    /// looking for a missing Shiny.Obd registration when what is missing is the platform BLE package's
    /// own <c>AddBluetoothLE()</c> call.
    /// </remarks>
    static IBleManager GetBleManager(IServiceProvider sp)
        => sp.GetService<IBleManager>() ?? throw new ObdException(
            "No IBleManager is registered. AddShinyObdBluetoothLE() registers the OBD pieces but " +
            "cannot register the BLE manager itself outside iOS and Android — call " +
            "services.AddBluetoothLE() from your platform package as well: Shiny.BluetoothLE.Linux " +
            "on Linux, Shiny.BluetoothLE.Blazor on Blazor WebAssembly, or Shiny.BluetoothLE on a " +
            "Windows/Apple head."
        );
}
