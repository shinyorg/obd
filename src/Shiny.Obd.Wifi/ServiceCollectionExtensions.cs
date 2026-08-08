using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shiny.Obd;
using Shiny.Obd.Wifi;

namespace Shiny;

/// <summary>
/// WiFi OBD transport registration.
/// </summary>
public static class WifiObdServiceCollectionExtensions
{
    /// <summary>
    /// Registers the WiFi (TCP) OBD transport, scanner and configuration.
    /// </summary>
    /// <remarks>
    /// Supported everywhere - it is a plain socket, so iOS, Android, Windows, Linux and macOS all
    /// behave identically. There is no platform package to add and no permission to request beyond
    /// iOS's local network prompt (<c>NSLocalNetworkUsageDescription</c>).
    ///
    /// <para>
    /// The app is still responsible for being joined to the adapter's WiFi network, and on Android
    /// for pinning traffic to it - that network has no internet, so the OS keeps the default route on
    /// cellular unless told otherwise. See the notes on <see cref="WifiObdTransport"/>.
    /// </para>
    ///
    /// <para>
    /// Registers <see cref="IObdTransport"/> and <see cref="IObdConnection"/> as singletons - an OBD
    /// adapter is a single physical resource, and most WiFi adapters accept only one TCP client at a
    /// time, so a scoped or transient connection would have two consumers locking each other out.
    /// Everything is registered with <c>TryAdd</c>, so calling this alongside
    /// <c>AddShinyObdSerial()</c> or <c>AddShinyObdBluetoothLE()</c> leaves whichever ran first in
    /// place rather than silently swapping the transport out. If you want a fallback chain across
    /// transports, construct them yourself instead of registering more than one.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddShinyObdWifi(
        this IServiceCollection services,
        Action<WifiObdConfiguration>? configure = null
    )
    {
        var config = new WifiObdConfiguration();
        configure?.Invoke(config);

        services.TryAddSingleton(config);
        services.TryAddSingleton<IObdDeviceScanner, WifiObdDeviceScanner>();
        services.TryAddSingleton<IObdTransport>(sp => new WifiObdTransport(
            sp.GetRequiredService<WifiObdConfiguration>(),
            sp.GetService<Microsoft.Extensions.Logging.ILogger<WifiObdTransport>>()
        ));
        services.TryAddSingleton<IObdConnection>(sp => new ObdConnection(sp.GetRequiredService<IObdTransport>()));

        return services;
    }

    /// <summary>
    /// Registers the WiFi OBD transport against a known address, skipping endpoint detection.
    /// </summary>
    public static IServiceCollection AddShinyObdWifi(this IServiceCollection services, string host, int port = 35000)
        => services.AddShinyObdWifi(x =>
        {
            x.Host = host;
            x.Port = port;
            x.AutoDetectEndpoint = false;
        });
}
