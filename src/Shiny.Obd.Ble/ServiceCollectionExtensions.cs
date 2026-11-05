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
    /// Registers the BLE OBD transport, device scanner and configuration — and the Shiny.BluetoothLE
    /// manager underneath them.
    /// </summary>
    /// <remarks>
    /// Works on every platform Shiny.BluetoothLE supports — iOS, Android, Mac Catalyst, macOS,
    /// Windows, Linux (BlueZ) and Blazor WebAssembly (Web Bluetooth).
    ///
    /// <para>
    /// This calls Shiny.BluetoothLE's <c>services.AddBluetoothLE()</c> for you, so on iOS, Android,
    /// Mac Catalyst, macOS, Windows and Blazor WebAssembly there is nothing else to register.
    /// <c>AddBluetoothLE()</c> is idempotent, so an app that also calls it itself is unaffected.
    /// </para>
    ///
    /// <para>
    /// <b>Linux is the one exception:</b> its BlueZ manager lives in the separate
    /// <c>Shiny.BluetoothLE.Linux</c> package (kept apart so its D-Bus dependency never reaches a
    /// WebAssembly app). On Linux use <c>AddShinyObdLinuxBluetoothLE()</c> from the
    /// <c>Shiny.Obd.Ble.Linux</c> package instead, which registers both.
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
        // Every Shiny.BluetoothLE target exposes AddBluetoothLE() and it no-ops when a manager is
        // already registered. On the plain net10.0 target it registers the Web Bluetooth manager when
        // running in a browser and does nothing elsewhere — Linux brings its own manager via
        // AddLinuxBluetoothLE(). This package ships a build for each platform head
        // (android/ios/maccatalyst/macos/windows) so this call binds to that platform's overload.
        services.AddBluetoothLE();

        services.TryAddSingleton(configuration ?? new BleObdConfiguration());

        // Factory rather than by type, so a missing BLE manager surfaces GetBleManager's actionable
        // error instead of the container's generic "unable to resolve service" — the scanner is
        // usually the first thing an app resolves.
        services.TryAddSingleton<IObdDeviceScanner>(sp => new BleObdDeviceScanner(
            GetBleManager(sp),
            sp.GetRequiredService<BleObdConfiguration>(),
            sp.GetService<Microsoft.Extensions.Logging.ILogger<BleObdDeviceScanner>>()
        ));

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
    /// After <c>AddShinyObdBluetoothLE()</c> calls <c>AddBluetoothLE()</c> itself, the only way to get
    /// here is a non-browser <c>net10.0</c> app — in practice Linux without <c>AddLinuxBluetoothLE()</c>.
    /// </remarks>
    static IBleManager GetBleManager(IServiceProvider sp)
        => sp.GetService<IBleManager>() ?? throw new ObdException(
            "No IBleManager is registered. AddShinyObdBluetoothLE() registers the BLE manager on iOS, " +
            "Android, Mac Catalyst, macOS, Windows and Blazor WebAssembly, but not on Linux — reference " +
            "Shiny.Obd.Ble.Linux and call services.AddShinyObdLinuxBluetoothLE() instead (or call " +
            "Shiny.BluetoothLE.Linux's services.AddLinuxBluetoothLE() alongside this)."
        );
}
