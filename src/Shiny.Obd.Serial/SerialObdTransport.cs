using System;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Shiny.Obd.Serial;

/// <summary>
/// Serial transport for OBD communication over USB or a UART. Works with any ELM327-compatible
/// adapter that presents as a serial port - OBDLink SX/EX, ELM327 clones on CH340/FTDI/CP210x, and
/// adapters wired directly to a board's TX/RX pins.
/// </summary>
/// <remarks>
/// Built on <c>System.IO.Ports</c>: Windows, Linux, macOS and Mac Catalyst.
///
/// <para>
/// <b>Not usable on Android or iOS.</b> iOS throws <c>PlatformNotSupportedException</c>. Android is
/// the trap — the assembly is not marked unsupported there and the native library ships for the
/// <c>android-*</c> RIDs, so a <c>net10.0-android</c> project compiles cleanly and then fails at
/// runtime with <c>UnauthorizedAccessException</c>, because <c>/dev/ttyUSB*</c> is <c>root:usb</c>
/// and unreachable from an app on an unrooted device. Android USB adapters need the USB Host API,
/// which would be a separate transport. Use <c>Shiny.Obd.Ble</c> on mobile.
/// </para>
///
/// <para>
/// For a permanently installed device this is the transport to prefer over BLE: there is no
/// pairing, no scan, no reconnect storm after an ignition cycle, and a wired adapter cannot wander
/// out of range.
/// </para>
///
/// <para>
/// Implements <see cref="IDisposable"/> as well as <see cref="IAsyncDisposable"/> so a DI container
/// can tear it down on its synchronous disposal path — see the note on <see cref="ObdConnection"/>.
/// Everything this closes is synchronous anyway, so the two are equivalent.
/// </para>
/// </remarks>
public class SerialObdTransport : IObdTransport, IDisposable
{
    readonly SerialObdConfiguration config;
    readonly ILogger logger;
    readonly SemaphoreSlim sendLock = new(1, 1);
    readonly StringBuilder responseBuffer = new();

    /// <summary>
    /// Guards <see cref="responseBuffer"/> and <see cref="responseTcs"/>, which the read pump and the
    /// sending thread both touch.
    /// </summary>
    readonly object exchangeLock = new();

    SerialPort? port;
    CancellationTokenSource? readPumpCts;
    Task? readPump;

    public SerialObdTransport(SerialObdConfiguration config, ILogger<SerialObdTransport>? logger = null)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        this.logger = logger ?? NullLogger<SerialObdTransport>.Instance;
    }

    /// <summary>
    /// Create a transport for a specific port, taking all other settings from the defaults.
    /// </summary>
    public SerialObdTransport(string portName, ILogger<SerialObdTransport>? logger = null)
        : this(new SerialObdConfiguration { PortName = portName }, logger)
    {
    }

    /// <summary>
    /// Create a transport from a device discovered by <see cref="SerialObdDeviceScanner"/>.
    /// </summary>
    public SerialObdTransport(ObdDiscoveredDevice device, SerialObdConfiguration config, ILogger<SerialObdTransport>? logger = null)
        : this(Clone(config, device?.Id ?? throw new ArgumentNullException(nameof(device))), logger)
    {
    }

    public bool IsConnected => this.port?.IsOpen == true;

    /// <summary>
    /// The port actually opened. Differs from <see cref="SerialObdConfiguration.PortName"/> when the
    /// port was discovered rather than configured.
    /// </summary>
    public string? ConnectedPortName { get; private set; }

    /// <summary>
    /// The baud rate actually in use - the probed one when
    /// <see cref="SerialObdConfiguration.AutoDetectBaudRate"/> is on.
    /// </summary>
    public int? ConnectedBaudRate { get; private set; }

    public async Task Connect(CancellationToken ct = default)
    {
        if (this.IsConnected)
            return;

        var portName = this.config.PortName ?? this.DiscoverPort();

        if (this.config.AutoDetectBaudRate)
        {
            await this.ConnectWithBaudProbe(portName, ct).ConfigureAwait(false);
        }
        else
        {
            await this.OpenPort(portName, this.config.BaudRate, ct).ConfigureAwait(false);
        }

        this.logger.LogInformation(
            "Connected to OBD adapter on {Port} at {Baud} baud",
            this.ConnectedPortName,
            this.ConnectedBaudRate
        );
    }

    public Task Disconnect()
    {
        // A disconnect is an answer. Fail anything mid-exchange now rather than leaving the caller to
        // sit out the full command timeout for a reply that can no longer arrive.
        this.EndExchange(new ObdException("The OBD connection was closed"));
        this.StopReadPump();
        this.ClosePort();
        return Task.CompletedTask;
    }

    public async Task<string> Send(string command, CancellationToken ct = default)
    {
        var active = this.port;
        if (active?.IsOpen != true)
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

            var bytes = Encoding.ASCII.GetBytes(command);
            await active.BaseStream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await active.BaseStream.FlushAsync(ct).ConfigureAwait(false);

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

    public void Dispose()
    {
        _ = this.Disconnect();
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        _ = this.Disconnect();

        // sendLock is deliberately left undisposed: a Send may still be unwinding on it, and
        // disposing a semaphore out from under a waiter throws ObjectDisposedException in the
        // caller's face.
        return default;
    }

    TaskCompletionSource<string>? responseTcs;

    string DiscoverPort()
    {
        var candidates = SerialPortEnumerator.Discover();

        var match = candidates.FirstOrDefault(x =>
            this.config.PortNameFilter == null ||
            x.Description.Contains(this.config.PortNameFilter, StringComparison.OrdinalIgnoreCase) ||
            x.PortName.Contains(this.config.PortNameFilter, StringComparison.OrdinalIgnoreCase)
        );

        if (match == null)
        {
            throw new ObdException(
                candidates.Count == 0
                    ? $"No serial ports found on {SerialPortEnumerator.PlatformHint}. Is the adapter plugged in, and does this user have permission to open it? (Linux: add the user to the 'dialout' group.)"
                    : $"No serial port matched filter '{this.config.PortNameFilter}'. Found: {string.Join(", ", candidates.Select(x => x.ToString()))}"
            );
        }

        this.logger.LogInformation("Selected serial port {Port} from {Count} candidate(s)", match, candidates.Count);

        // The by-id path is a symlink to the same device node, and holding the stable one means a
        // reconnect after a USB re-enumeration still lands on the right adapter.
        return match.StablePath ?? match.PortName;
    }

    async Task ConnectWithBaudProbe(string portName, CancellationToken ct)
    {
        // Try the configured rate first - when it is right (the common case) this costs nothing
        // beyond the single ATI the probe would have sent anyway.
        var order = new[] { this.config.BaudRate }
            .Concat(this.config.BaudRateCandidates)
            .Distinct()
            .ToArray();

        Exception? last = null;

        foreach (var baud in order)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await this.OpenPort(portName, baud, ct).ConfigureAwait(false);

                if (await this.ProbeAdapter(ct).ConfigureAwait(false))
                    return;

                this.logger.LogDebug("No sane adapter response at {Baud} baud", baud);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
                this.logger.LogDebug(ex, "Probe failed at {Baud} baud", baud);
            }

            this.StopReadPump();
            this.ClosePort();
        }

        var message = $"No ELM327-compatible adapter answered on {portName} at any of {string.Join(", ", order)} baud";
        throw last == null
            ? new ObdException(message)
            : new ObdException(message, last);
    }

    /// <summary>
    /// Whether something on the far end is speaking ELM327 at the current baud rate.
    /// </summary>
    /// <remarks>
    /// At the wrong baud rate a UART does not go quiet - it returns framing garbage, which is why
    /// this checks the shape of the reply rather than merely that one arrived. An identifier that is
    /// mostly printable ASCII is the signal; a run of high bytes is the tell for a mismatch.
    /// </remarks>
    async Task<bool> ProbeAdapter(CancellationToken ct)
    {
        try
        {
            // ATZ resets and can take the better part of a second to answer; its reply is discarded
            // because a reset adapter often echoes its banner over several writes.
            await this.SendProbe("ATZ\r", TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            var id = await this.SendProbe("ATI\r", TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(id))
                return false;

            if (id.Contains("ELM", StringComparison.OrdinalIgnoreCase) ||
                id.Contains("STN", StringComparison.OrdinalIgnoreCase) ||
                id.Contains("OBD", StringComparison.OrdinalIgnoreCase))
                return true;

            // Unbranded clones exist, so fall back to "did we get readable text".
            var printable = id.Count(c => c is >= ' ' and <= '~');
            return printable >= id.Length * 0.8;
        }
        catch (ObdTimeoutException)
        {
            return false;
        }
    }

    async Task<string> SendProbe(string command, TimeSpan timeout, CancellationToken ct)
    {
        var active = this.port ?? throw new ObdException("Port not open");

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (this.exchangeLock)
        {
            this.responseBuffer.Clear();
            this.responseTcs = tcs;
        }

        try
        {
            var bytes = Encoding.ASCII.GetBytes(command);
            await active.BaseStream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await active.BaseStream.FlushAsync(ct).ConfigureAwait(false);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try
            {
                return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new ObdTimeoutException(command.Trim(), timeout);
            }
        }
        finally
        {
            this.EndExchange();
        }
    }

    async Task OpenPort(string portName, int baudRate, CancellationToken ct)
    {
        var sp = new SerialPort(portName, baudRate, this.config.Parity, this.config.DataBits, this.config.StopBits)
        {
            Handshake = this.config.Handshake,
            DtrEnable = this.config.DtrEnable,
            RtsEnable = this.config.RtsEnable,

            // The read pump owns timing through its own cancellation, and BaseStream ignores these
            // anyway on Unix - but leaving them at the 0 default trips argument validation on some
            // runtimes, so they are set to something harmless.
            ReadTimeout = (int)this.config.CommandTimeout.TotalMilliseconds,
            WriteTimeout = (int)this.config.CommandTimeout.TotalMilliseconds
        };

        try
        {
            sp.Open();
        }
        catch (UnauthorizedAccessException ex)
        {
            sp.Dispose();

            // Android is the confusing one and is worth calling out by name. System.IO.Ports is not
            // marked unsupported there and its native library does ship for the android RIDs, so the
            // failure lands here rather than as a PlatformNotSupportedException - looking exactly
            // like a fixable permissions problem. It is not: the kernel does create /dev/ttyUSB0 for
            // a host-mode adapter, but it is root:usb and an app's UID is not in that group, so only
            // a rooted device can open it. The supported route is a different mechanism entirely.
            if (OperatingSystem.IsAndroid())
            {
                throw new ObdException(
                    $"Cannot open {portName} on Android. Serial device nodes are not accessible to apps on an unrooted device - USB adapters must go through the Android USB Host API (UsbManager), which is a different transport, not a permission fix. Use Shiny.Obd.Ble for a BLE adapter on Android.",
                    ex
                );
            }

            throw new ObdException(
                $"Permission denied opening {portName}. On Linux add the service user to the 'dialout' group (usermod -aG dialout <user>) and re-login; the port may also be held open by another process such as ModemManager.",
                ex
            );
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            sp.Dispose();
            throw new ObdException($"Could not open serial port {portName} at {baudRate} baud", ex);
        }

        this.port = sp;
        this.ConnectedPortName = portName;
        this.ConnectedBaudRate = baudRate;

        this.StartReadPump();

        // USB-serial bridges that reset when DTR is raised drop everything sent during the reset.
        if (this.config.OpenSettleDelay > TimeSpan.Zero)
            await Task.Delay(this.config.OpenSettleDelay, ct).ConfigureAwait(false);

        this.DiscardStale();
    }

    void StartReadPump()
    {
        this.readPumpCts = new CancellationTokenSource();
        this.readPump = Task.Run(() => this.ReadPump(this.port!, this.readPumpCts.Token));
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
        this.readPump = null;
    }

    /// <summary>
    /// Continuously drains the port into the current exchange's buffer.
    /// </summary>
    /// <remarks>
    /// A pump rather than a read-per-command because an ELM327 does not answer in one write: a
    /// multi-frame CAN reply arrives as several chunks, and the adapter also emits unsolicited text
    /// (the reset banner, "SEARCHING..."). Reading continuously and completing on the '>' prompt is
    /// what makes those cases fall out naturally.
    /// </remarks>
    async Task ReadPump(SerialPort sp, CancellationToken ct)
    {
        var buffer = new byte[512];

        while (!ct.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await sp.BaseStream.ReadAsync(buffer, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // The cable was pulled, or the port was closed under us. Fail whoever is waiting
                // rather than letting them sit out the full command timeout.
                this.EndExchange(new ObdException("The serial connection was lost", ex));
                return;
            }

            if (read <= 0)
            {
                // EOF on a serial port means the device went away.
                this.EndExchange(new ObdException("The serial connection was closed by the device"));
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
    /// Drops anything the adapter said before we started listening (reset banners, half a reply from
    /// a previous process) so it cannot be mistaken for the answer to the first real command.
    /// </summary>
    void DiscardStale()
    {
        lock (this.exchangeLock)
            this.responseBuffer.Clear();

        try
        {
            this.port?.DiscardInBuffer();
            this.port?.DiscardOutBuffer();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // Not every platform implements the discard ioctls; the buffer clear above is the part
            // that matters.
        }
    }

    void ClosePort()
    {
        var sp = this.port;
        this.port = null;
        this.ConnectedPortName = null;
        this.ConnectedBaudRate = null;

        if (sp == null)
            return;

        try
        {
            if (sp.IsOpen)
                sp.Close();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Closing a port whose device has already been unplugged throws on some platforms.
            this.logger.LogDebug(ex, "Error closing serial port");
        }
        finally
        {
            sp.Dispose();
        }
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

    static SerialObdConfiguration Clone(SerialObdConfiguration source, string portName) => new()
    {
        PortName = portName,
        BaudRate = source.BaudRate,
        BaudRateCandidates = source.BaudRateCandidates,
        AutoDetectBaudRate = source.AutoDetectBaudRate,
        Parity = source.Parity,
        DataBits = source.DataBits,
        StopBits = source.StopBits,
        Handshake = source.Handshake,
        DtrEnable = source.DtrEnable,
        RtsEnable = source.RtsEnable,
        CommandTimeout = source.CommandTimeout,
        OpenSettleDelay = source.OpenSettleDelay,
        PortNameFilter = source.PortNameFilter
    };
}
