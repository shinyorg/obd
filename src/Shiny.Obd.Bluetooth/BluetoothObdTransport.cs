using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shiny.Bluetooth;

namespace Shiny.Obd.Bluetooth;

/// <summary>
/// Classic Bluetooth transport for OBD communication over RFCOMM (Serial Port Profile). Works with any
/// ELM327-compatible classic adapter - the ELM327 v1.5 "Bluetooth" clones, OBDLink LX/MX+, Vgate iCar,
/// Veepeak BT.
/// </summary>
/// <remarks>
/// Built on <c>Shiny.Bluetooth</c>: Android, Windows, macOS, and Linux through
/// <c>Shiny.Bluetooth.Linux</c>.
///
/// <para>
/// <b>Not usable on iOS or Mac Catalyst.</b> Classic Bluetooth there is ExternalAccessory, which only
/// reaches MFi-certified accessories, and no ELM327 clone is one. Use <c>Shiny.Obd.Ble</c> with a BLE
/// adapter on Apple mobile.
/// </para>
///
/// <para>
/// A classic adapter is an SPP-to-UART bridge, so on the wire this is the same as the serial and WiFi
/// transports: bytes in, bytes out, terminated by the '&gt;' prompt. What differs is getting there -
/// the adapter must be paired (most clones use PIN <c>1234</c> or <c>0000</c>), and there is no
/// service UUID to scan for, so an adapter is picked by address or by name.
/// </para>
///
/// <para>
/// <b>There is deliberately no auto-reconnect.</b> A dropped RFCOMM link loses the ELM327's session
/// state (ATE0, ATS1, the negotiated protocol) with it, so a silent redial would hand back a live
/// connection whose replies parse as garbage. A lost link surfaces as <see cref="ObdException"/>, and
/// the fix is to run <see cref="ObdConnection.Connect"/> again, which re-initialises the adapter.
/// </para>
///
/// <para>
/// Implements <see cref="IDisposable"/> as well as <see cref="IAsyncDisposable"/> so a DI container
/// can tear it down on its synchronous disposal path - see the note on <see cref="ObdConnection"/>.
/// </para>
/// </remarks>
public class BluetoothObdTransport : IObdTransport, IDisposable
{
    readonly IBluetoothManager manager;
    readonly BluetoothObdConfiguration config;
    readonly IBluetoothDevice? knownDevice;
    readonly ILogger logger;
    readonly SemaphoreSlim sendLock = new(1, 1);
    readonly StringBuilder responseBuffer = new();

    /// <summary>
    /// Guards <see cref="responseBuffer"/> and <see cref="responseTcs"/>, which the read pump and the
    /// sending thread both touch.
    /// </summary>
    readonly object exchangeLock = new();

    Stream? stream;
    CancellationTokenSource? readPumpCts;
    TaskCompletionSource<string>? responseTcs;
    volatile bool linkUp;

    /// <summary>
    /// Create a transport that picks its adapter from <see cref="BluetoothObdConfiguration.Address"/>, or
    /// failing that the paired devices.
    /// </summary>
    public BluetoothObdTransport(
        IBluetoothManager manager,
        BluetoothObdConfiguration config,
        ILogger<BluetoothObdTransport>? logger = null
    )
    {
        this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        this.logger = logger ?? NullLogger<BluetoothObdTransport>.Instance;
    }

    /// <summary>
    /// Create a transport for a specific adapter address, taking all other settings from the defaults.
    /// </summary>
    public BluetoothObdTransport(IBluetoothManager manager, string address, ILogger<BluetoothObdTransport>? logger = null)
        : this(manager, new BluetoothObdConfiguration { Address = address }, logger)
    {
    }

    /// <summary>
    /// Create a transport from a device discovered by <see cref="BluetoothObdDeviceScanner"/>.
    /// </summary>
    public BluetoothObdTransport(
        IBluetoothManager manager,
        ObdDiscoveredDevice device,
        BluetoothObdConfiguration config,
        ILogger<BluetoothObdTransport>? logger = null
    )
        : this(manager, config, logger)
    {
        this.knownDevice = Device(device);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Tracked from the read pump, so it goes false as soon as the adapter drops the link rather than
    /// on the next write.
    /// </remarks>
    public bool IsConnected => this.linkUp;

    /// <summary>
    /// The adapter actually connected to. Differs from <see cref="BluetoothObdConfiguration.Address"/>
    /// when the adapter was picked from the paired devices rather than configured.
    /// </summary>
    public IBluetoothDevice? ConnectedDevice { get; private set; }

    /// <summary>
    /// Opens the RFCOMM stream to a device. Replaceable by tests, because <see cref="BluetoothConnection"/>
    /// cannot be constructed outside Shiny.Bluetooth.
    /// </summary>
    internal Func<IBluetoothDevice, BluetoothObdConfiguration, CancellationToken, Task<Stream>> OpenStream { get; set; }
        = async (device, cfg, ct) => (await device.Connect(cfg.ServiceUuid, cfg.Secure, ct).ConfigureAwait(false)).Stream;

    /// <inheritdoc/>
    public async Task Connect(CancellationToken ct = default)
    {
        if (this.IsConnected)
            return;

        // The adapter may have dropped a previous link on its own; release what is left of it before
        // opening a new one.
        this.StopReadPump();
        this.CloseStream();

        var access = await this.manager.RequestAccess(ct).ConfigureAwait(false);
        if (access != AccessState.Available)
            throw new ObdException(ExplainAccess(access));

        var device = this.knownDevice ?? await this.ResolveDevice(ct).ConfigureAwait(false);
        var connected = await this.OpenWithRetry(device, ct).ConfigureAwait(false);

        this.stream = connected;
        this.ConnectedDevice = device;
        this.linkUp = true;

        this.StartReadPump();
        this.DiscardStale();

        this.logger.LogInformation("Connected to OBD adapter {Name} ({Address})", device.Name, device.Address);
    }

    /// <inheritdoc/>
    public Task Disconnect()
    {
        // A disconnect is an answer. Fail anything mid-exchange now rather than leaving the caller to
        // sit out the full command timeout for a reply that can no longer arrive.
        this.EndExchange(new ObdException("The OBD connection was closed"));
        this.StopReadPump();
        this.CloseStream();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<string> Send(string command, CancellationToken ct = default)
    {
        var active = this.stream;
        if (!this.IsConnected || active == null)
            throw new ObdException("Not connected to OBD adapter");

        await this.sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (this.exchangeLock)
            {
                this.responseBuffer.Clear();
                this.responseTcs = tcs;
            }

            try
            {
                var bytes = Encoding.ASCII.GetBytes(command);
                await active.WriteAsync(bytes, ct).ConfigureAwait(false);
                await active.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                this.MarkLinkDown();
                throw new ObdException("The Bluetooth connection to the OBD adapter was lost", ex);
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(this.config.CommandTimeout);

            try
            {
                return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Our own deadline elapsed, not the caller's token. Report it as what it is so a
                // polling caller doesn't mistake a quiet adapter for its own shutdown.
                throw new ObdTimeoutException(command.Trim(), this.config.CommandTimeout);
            }
        }
        finally
        {
            // However this ended, the exchange is over. Clearing it before releasing the lock is what
            // stops a late reply to a timed-out command from completing the *next* command's wait
            // with the previous command's data - one timeout would otherwise put every response
            // after it off by one, and the session would never recover on its own.
            this.EndExchange();
            this.sendLock.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _ = this.Disconnect();
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _ = this.Disconnect();

        // sendLock is deliberately left undisposed: a Send may still be unwinding on it, and
        // disposing a semaphore out from under a waiter throws ObjectDisposedException in the
        // caller's face.
        return default;
    }

    async Task<IBluetoothDevice> ResolveDevice(CancellationToken ct)
    {
        if (this.config.Address != null)
        {
            try
            {
                return await this.manager.GetDevice(this.config.Address, ct).ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                throw new ObdException($"'{this.config.Address}' is not a Bluetooth address - expected AA:BB:CC:DD:EE:FF", ex);
            }
        }

        var paired = await this.manager.GetPairedDevices(ct).ConfigureAwait(false);
        var match = paired.FirstOrDefault(x => this.config.Matches(x.Name));

        if (match == null)
        {
            throw new ObdException(
                paired.Count == 0
                    ? "No paired Bluetooth devices. Pair the OBD adapter in the system Bluetooth settings first (most use PIN 1234 or 0000), or find it with BluetoothObdDeviceScanner."
                    : $"No paired Bluetooth device looks like an OBD adapter (filters: {String.Join(", ", this.config.DeviceNameFilters)}). Paired: {String.Join(", ", paired.Select(x => $"{x.Name ?? "(unnamed)"} [{x.Address}]"))}. Set BluetoothObdConfiguration.Address, or adjust DeviceNameFilters."
            );
        }

        this.logger.LogInformation("Selected paired device {Name} ({Address}) from {Count} paired", match.Name, match.Address, paired.Count);
        return match;
    }

    async Task<Stream> OpenWithRetry(IBluetoothDevice device, CancellationToken ct)
    {
        var attempts = Math.Max(1, this.config.ConnectAttempts);
        Exception? last = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(this.config.ConnectTimeout);

            try
            {
                return await this.OpenStream(device, this.config, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                last = ex;
                this.logger.LogDebug("Connect attempt {Attempt} to {Address} timed out", attempt, device.Address);
            }
            catch (BluetoothConnectionException ex)
            {
                last = ex;
                this.logger.LogDebug(ex, "Connect attempt {Attempt} to {Address} failed", attempt, device.Address);
            }
            catch (BluetoothPermissionException ex)
            {
                // Retrying cannot grant a permission.
                throw new ObdException(ex.Message, ex);
            }
            catch (BluetoothNotSupportedException ex)
            {
                throw new ObdException($"Classic Bluetooth connections are not supported on this platform - {ex.Message}", ex);
            }

            if (attempt < attempts && this.config.ConnectRetryDelay > TimeSpan.Zero)
                await Task.Delay(this.config.ConnectRetryDelay, ct).ConfigureAwait(false);
        }

        throw new ObdException(
            $"Could not connect to {device.Name ?? "the OBD adapter"} ({device.Address}) after {attempts} attempt(s). Is the ignition on, the adapter in range and not connected to another phone? If it has never been paired, pair it in the system Bluetooth settings (most use PIN 1234 or 0000).",
            last!
        );
    }

    static string ExplainAccess(AccessState access) => access switch
    {
        AccessState.Disabled => "Bluetooth is turned off",
        AccessState.NotSupported => "Classic Bluetooth is not supported on this device. On iOS and Mac Catalyst use Shiny.Obd.Ble with a BLE adapter.",
        AccessState.NotSetup => "Bluetooth permissions are missing from the app manifest (Android: BLUETOOTH_CONNECT and BLUETOOTH_SCAN; macOS: NSBluetoothAlwaysUsageDescription)",
        _ => $"Bluetooth access was not granted ({access})"
    };

    void StartReadPump()
    {
        this.readPumpCts = new CancellationTokenSource();
        var token = this.readPumpCts.Token;
        var active = this.stream!;
        _ = Task.Run(() => this.ReadPump(active, token), CancellationToken.None);
    }

    void StopReadPump()
    {
        try
        {
            this.readPumpCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        this.readPumpCts?.Dispose();
        this.readPumpCts = null;
    }

    /// <summary>
    /// Continuously drains the RFCOMM stream into the current exchange's buffer.
    /// </summary>
    /// <remarks>
    /// A pump rather than a read-per-command because RFCOMM, like TCP, preserves no message boundaries:
    /// a multi-frame CAN reply arrives as several reads, and the adapter also emits unsolicited text
    /// (the reset banner, "SEARCHING..."). Reading continuously and completing on the '&gt;' prompt is
    /// what makes those cases fall out naturally.
    /// </remarks>
    async Task ReadPump(Stream source, CancellationToken ct)
    {
        var buffer = new byte[512];

        while (!ct.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // Out of range, ignition off, or the stream was closed under us. Fail whoever is
                // waiting rather than letting them sit out the full command timeout.
                if (!ct.IsCancellationRequested)
                    this.MarkLinkDown(new ObdException("The Bluetooth connection to the OBD adapter was lost", ex));
                return;
            }

            if (read <= 0)
            {
                // A remote disconnect - most often the adapter losing power with the ignition.
                this.MarkLinkDown(new ObdException("The OBD adapter closed the Bluetooth connection"));
                return;
            }

            var text = Encoding.ASCII.GetString(buffer, 0, read);
            lock (this.exchangeLock)
            {
                // Nothing is waiting - this is a late reply to a command that already timed out, or
                // the adapter's power-on banner. Dropping it keeps it out of the next command's buffer.
                if (this.responseTcs == null)
                    continue;

                this.responseBuffer.Append(text);

                var current = this.responseBuffer.ToString();
                if (!current.Contains('>'))
                    continue;

                var response = current.Replace(">", "").Trim();
                this.responseTcs.TrySetResult(response);
            }
        }
    }

    /// <summary>
    /// Drops anything the adapter said before we started listening (a power-on banner, the tail of a
    /// previous client's session) so it cannot be mistaken for the answer to the first real command.
    /// </summary>
    void DiscardStale()
    {
        lock (this.exchangeLock)
            this.responseBuffer.Clear();
    }

    void CloseStream()
    {
        var active = this.stream;
        this.stream = null;
        this.ConnectedDevice = null;
        this.linkUp = false;

        try
        {
            // Disposing the stream is what closes the RFCOMM channel - BluetoothConnection holds
            // nothing else.
            active?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Closing a channel whose adapter has already gone throws on some platforms.
            this.logger.LogDebug(ex, "Error closing Bluetooth stream");
        }
    }

    /// <summary>Records that the link is gone and fails whoever was waiting on it.</summary>
    void MarkLinkDown(Exception? error = null)
    {
        this.linkUp = false;
        this.EndExchange(error);
    }

    /// <summary>Closes off the current exchange, optionally failing whoever is waiting on it.</summary>
    void EndExchange(Exception? error = null)
    {
        lock (this.exchangeLock)
        {
            if (error != null)
                this.responseTcs?.TrySetException(error);

            this.responseTcs = null;
            this.responseBuffer.Clear();
        }
    }

    static IBluetoothDevice Device(ObdDiscoveredDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (device.NativeDevice is IBluetoothDevice native)
            return native;

        throw new ObdException(
            $"'{device.Name}' did not come from a BluetoothObdDeviceScanner - its NativeDevice is {device.NativeDevice?.GetType().Name ?? "null"}, not an IBluetoothDevice."
        );
    }
}
