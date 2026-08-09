using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shiny.Obd.Emulator;

namespace Shiny;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the OBD-II adapter emulator: the emulated vehicle, the ELM327 responder, the TCP
    /// front-end and the driving scenario player.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing listens until you call <see cref="ObdEmulatorHost.Start"/>. Resolve
    /// <see cref="ObdEmulatorHost"/> to start and stop it, <see cref="ObdEmulatorState"/> to set
    /// values and fault codes, and <see cref="DrivingScenarioPlayer"/> to play a drive into it.
    /// </para>
    /// <para>
    /// <b>Call this from your UI thread if you have one.</b> The
    /// <see cref="SynchronizationContext"/> in force here is captured and used to marshal every state
    /// change a transport or a running scenario makes, so bindings are only ever touched from the
    /// thread that owns them. In a console or test host there is no context and everything runs
    /// inline. Register your own <see cref="IObdEmulatorDispatcher"/> beforehand to override this.
    /// </para>
    /// <para>
    /// mDNS announcement is optional — call Shiny's <c>AddMdns()</c> as well and the TCP server
    /// publishes itself as <c>_obd._tcp</c>; without it the server still listens, it just has to be
    /// found by address.
    /// </para>
    /// <para>
    /// BLE is a separate package: add <c>Shiny.Obd.Emulator.Ble</c> and call
    /// <c>AddObdEmulatorBluetoothLE()</c> as well to advertise as a Bluetooth LE adapter. Transports
    /// are additive, so registering both means one vehicle answered on both.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional. Changes ports, UUIDs and the advertised name.</param>
    public static IServiceCollection AddObdEmulator(
        this IServiceCollection services,
        Action<ObdEmulatorConfiguration>? configure = null
    )
    {
        var config = new ObdEmulatorConfiguration();
        configure?.Invoke(config);

        services.TryAddSingleton(config);

        // Captured here rather than in the host's constructor: DI resolves lazily and on whichever
        // thread first asks, which is rarely the UI thread. Registration almost always runs on it.
        services.TryAddSingleton<IObdEmulatorDispatcher>(new SynchronizationContextDispatcher());

        services.TryAddSingleton<ObdEmulatorState>();
        services.TryAddSingleton<Elm327Responder>();

        // Singleton so a drive keeps playing while the UI is somewhere else - which is the whole
        // point of the longer scenarios.
        services.TryAddSingleton<DrivingScenarioPlayer>();

        services.TryAddSingleton<TcpObdServer>();
        services.AddSingleton<IObdEmulatorTransport>(sp => sp.GetRequiredService<TcpObdServer>());

        services.TryAddSingleton<ObdEmulatorHost>();
        return services;
    }
}
