namespace Shiny.Obd.Emulator;

/// <summary>
/// One way a client can reach the emulated vehicle - a TCP listener, a BLE peripheral, or something
/// you write yourself.
/// </summary>
/// <remarks>
/// <para>
/// Every transport does the same job: frame the bytes a client sends into ELM327 commands, hand each
/// to <see cref="Elm327Responder"/>, and write back what it returns. The vehicle itself is shared, so
/// a value set on one transport is answered identically on the others - two clients on different
/// transports see one car.
/// </para>
/// <para>
/// <see cref="ObdEmulatorHost"/> resolves every registered transport and starts them independently:
/// a device with Bluetooth switched off should still come up as a WiFi adapter, so one failing to
/// start never stops the others.
/// </para>
/// </remarks>
public interface IObdEmulatorTransport
{
    /// <summary>Short label used in the log and the client list, e.g. "BLE" or "TCP".</summary>
    string Name { get; }

    /// <summary>Whether this transport is currently accepting clients.</summary>
    bool IsRunning { get; }

    /// <summary>One line on what the transport is doing, or why it is not running.</summary>
    string Status { get; }

    /// <summary>
    /// Anything worth showing beneath the status - the addresses a client can connect to, an mDNS
    /// registration. Empty when there is nothing to add.
    /// </summary>
    IReadOnlyList<string> Details { get; }

    /// <summary>
    /// Starts accepting clients. Returns false when the transport cannot run here - Bluetooth is off,
    /// the port is taken - having put the reason in <see cref="Status"/>. Failing to start is a normal
    /// outcome, not an exception.
    /// </summary>
    Task<bool> Start(IObdEmulatorSink sink, CancellationToken cancellationToken = default);

    /// <summary>Stops accepting clients and drops the ones connected. Safe to call when not running.</summary>
    Task Stop();
}
