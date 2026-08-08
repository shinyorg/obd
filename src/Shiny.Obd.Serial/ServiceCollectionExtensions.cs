using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shiny.Obd;
using Shiny.Obd.Serial;

namespace Shiny;

public static class SerialObdServiceCollectionExtensions
{
    /// <summary>
    /// Registers the serial (USB/UART) OBD transport, scanner and configuration.
    /// </summary>
    /// <remarks>
    /// Supported on Windows, Linux, macOS and Mac Catalyst. Not usable on Android or iOS — see the
    /// platform notes on <see cref="SerialObdTransport"/>; use
    /// <see cref="ServiceCollectionExtensions.AddShinyObdBluetoothLE"/> there.
    ///
    /// <para>
    /// Registers <see cref="IObdTransport"/> and <see cref="IObdConnection"/> as singletons - an OBD
    /// adapter is a single physical resource, and a scoped or transient connection would have two
    /// consumers fighting over one serial port. Everything is registered with <c>TryAdd</c>, so
    /// calling both this and <c>AddShinyObdBluetoothLE()</c> leaves whichever ran first in place
    /// rather than silently swapping the transport out. If you want a fallback chain across
    /// transports, construct them yourself instead of registering both.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddShinyObdSerial(
        this IServiceCollection services,
        Action<SerialObdConfiguration>? configure = null
    )
    {
        var config = new SerialObdConfiguration();
        configure?.Invoke(config);

        services.TryAddSingleton(config);
        services.TryAddSingleton<IObdDeviceScanner, SerialObdDeviceScanner>();
        services.TryAddSingleton<IObdTransport, SerialObdTransport>();
        services.TryAddSingleton<IObdConnection>(sp => new ObdConnection(sp.GetRequiredService<IObdTransport>()));

        return services;
    }

    /// <summary>
    /// Registers the serial OBD transport against a known port.
    /// </summary>
    public static IServiceCollection AddShinyObdSerial(this IServiceCollection services, string portName)
        => services.AddShinyObdSerial(x => x.PortName = portName);
}
