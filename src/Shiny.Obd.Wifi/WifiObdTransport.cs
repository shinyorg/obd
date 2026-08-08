using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Shiny.Obd.Wifi;

/// <summary>
/// WiFi transport for OBD communication. Works with any ELM327-compatible adapter that exposes a raw
/// TCP socket - OBDLink MX Wi-Fi, Veepeak WiFi, Vgate iCar, and the ESP8266/ESP32-based clones.
/// </summary>
/// <remarks>
/// A WiFi OBD adapter is a TCP-to-UART bridge: it runs its own access point, you join it, and it
/// hands you the ELM327's serial stream over a socket. There is no framing, no handshake and no
/// protocol on top - bytes in, bytes out, terminated by the '&gt;' prompt.
///
/// <para>
/// <b>This is the only transport with no platform story.</b> It is a plain socket, so it behaves
/// identically on iOS, Android, Windows, Linux and macOS. Serial cannot be used on iOS or Android at
/// all, and BLE needs a BLE adapter and pairing; WiFi needs neither.
/// </para>
///
/// <para>
/// <b>Two things bite, and neither is a bug in this code.</b>
/// </para>
///
/// <para>
/// <i>Routing.</i> The adapter's access point has no internet, so the OS may decline to route
/// through it. Android is the pointed case: it keeps the default route on cellular and the socket
/// then connects to nothing at all. The app must pin traffic to the adapter's network -
/// <c>ConnectivityManager.BindProcessToNetwork(network)</c> for the whole process, or bind the socket
/// itself through <see cref="WifiObdConfiguration.ConfigureSocket"/>. On iOS, joining an AP with no
/// internet is fine, but the app needs <c>NSLocalNetworkUsageDescription</c> and the user's consent;
/// a denial is silent and looks exactly like a dead adapter.
/// </para>
///
/// <para>
/// <i>A TCP connect proves nothing.</i> Anything listening on the address accepts - your router on
/// 192.168.0.1 will happily complete a connect and then never say a word. That is why
/// <see cref="WifiObdConfiguration.AutoDetectEndpoint"/> validates a candidate with ATI rather than
/// trusting the connect, and why it is on by default.
/// </para>
///
/// <para>
/// <b>There is deliberately no auto-reconnect.</b> A dropped socket loses the ELM327's session state
/// - ATE0, ATS1, the negotiated protocol - because that state lives in the adapter, not in the
/// socket. Silently redialling would hand the caller a live connection with echo back on, whose
/// replies parse as garbage rather than failing outright. A lost link surfaces as
/// <see cref="ObdException"/>, and the fix is to run <see cref="ObdConnection.Connect"/> again, which
/// re-initialises the adapter. <see cref="WifiObdConfiguration.KeepAliveInterval"/> is what stops the
/// idle drop happening in the first place.
/// </para>
///
/// <para>
/// Implements <see cref="IDisposable"/> as well as <see cref="IAsyncDisposable"/> so a DI container
/// can tear it down on its synchronous disposal path - see the note on <see cref="ObdConnection"/>.
/// </para>
/// </remarks>
public class WifiObdTransport : IObdTransport, IDisposable
{
    readonly WifiObdConfiguration config;
    readonly ILogger logger;
    readonly SemaphoreSlim sendLock = new(1, 1);
    readonly StringBuilder responseBuffer = new();

    /// <summary>
    /// Guards <see cref="responseBuffer"/> and <see cref="responseTcs"/>, which the read pump and the
    /// sending thread both touch.
    /// </summary>
    readonly object exchangeLock = new();

    Socket? socket;
    NetworkStream? stream;
    CancellationTokenSource? readPumpCts;
    CancellationTokenSource? keepAliveCts;
    TaskCompletionSource<string>? responseTcs;
    volatile bool linkUp;
    long lastActivity;

    /// <summary>
    /// Create a transport from a full configuration.
    /// </summary>
    public WifiObdTransport(WifiObdConfiguration config, ILogger<WifiObdTransport>? logger = null)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        this.logger = logger ?? NullLogger<WifiObdTransport>.Instance;
    }

    /// <summary>
    /// Create a transport for a specific address, taking all other settings from the defaults.
    /// </summary>
    public WifiObdTransport(string host, int port = 35000, ILogger<WifiObdTransport>? logger = null)
        : this(new WifiObdConfiguration { Host = host, Port = port, AutoDetectEndpoint = false }, logger)
    {
    }

    /// <summary>
    /// Create a transport from a device discovered by <see cref="WifiObdDeviceScanner"/>.
    /// </summary>
    public WifiObdTransport(ObdDiscoveredDevice device, WifiObdConfiguration config, ILogger<WifiObdTransport>? logger = null)
        : this(Clone(config, Endpoint(device)), logger)
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Tracked from the read pump rather than read off the socket. <c>Socket.Connected</c> reports the
    /// state as of the last I/O, so it still says true for a link the adapter dropped while idle.
    /// </remarks>
    public bool IsConnected => this.linkUp;

    /// <summary>
    /// The endpoint actually connected to. Differs from <see cref="WifiObdConfiguration.Host"/> when
    /// the adapter was discovered rather than configured.
    /// </summary>
    public WifiObdEndpoint? ConnectedEndpoint { get; private set; }

    /// <summary>
    /// The adapter's ATI identity as seen during endpoint detection, when detection ran.
    /// </summary>
    public string? DetectedIdentifier { get; private set; }

    /// <inheritdoc/>
    public async Task Connect(CancellationToken ct = default)
    {
        if (this.IsConnected)
            return;

        var candidates = WifiObdProbe.BuildCandidates(this.config);
        if (candidates.Count == 0)
        {
            throw new ObdException(
                "No WiFi endpoint to connect to. Set WifiObdConfiguration.Host, or leave AutoDetectEndpoint on so the well-known adapter addresses are probed."
            );
        }

        // An explicit host with detection turned off is a promise that the adapter is there. Take it
        // at face value and skip the probe - it is the one case where the extra round trip buys
        // nothing, and it keeps a deliberately non-standard adapter that dislikes ATI usable.
        if (this.config.Host != null && !this.config.AutoDetectEndpoint)
        {
            await this.Open(candidates[0], ct).ConfigureAwait(false);
        }
        else
        {
            await this.ConnectWithProbe(candidates, ct).ConfigureAwait(false);
        }

        this.StartKeepAlive();
        this.logger.LogInformation("Connected to OBD adapter at {Endpoint}", this.ConnectedEndpoint);
    }

    /// <inheritdoc/>
    public Task Disconnect()
    {
        // A disconnect is an answer. Fail anything mid-exchange now rather than leaving the caller to
        // sit out the full command timeout for a reply that can no longer arrive.
        this.EndExchange(new ObdException("The OBD connection was closed"));
        this.StopKeepAlive();
        this.StopReadPump();
        this.CloseSocket();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<string> Send(string command, CancellationToken ct = default)
    {
        if (!this.IsConnected)
            throw new ObdException("Not connected to OBD adapter");

        await this.sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await this.Exchange(command, this.config.CommandTimeout, ct).ConfigureAwait(false);
        }
        finally
        {
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

    async Task ConnectWithProbe(IReadOnlyList<WifiObdEndpoint> candidates, CancellationToken ct)
    {
        Exception? last = null;

        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await this.Open(candidate, ct).ConfigureAwait(false);

                var identity = await this.ProbeAdapter(ct).ConfigureAwait(false);
                if (identity != null)
                {
                    this.DetectedIdentifier = identity;
                    return;
                }

                this.logger.LogDebug("Nothing ELM327-shaped answered at {Endpoint}", candidate);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
                this.logger.LogDebug(ex, "Probe failed at {Endpoint}", candidate);
            }

            this.StopReadPump();
            this.CloseSocket();
        }

        var message =
            $"No ELM327-compatible adapter answered at any of {String.Join(", ", candidates.Select(x => x.ToString()))}. " +
            "Is this device joined to the adapter's WiFi network?";

        throw last == null
            ? new ObdException(message)
            : new ObdException(message, last);
    }

    /// <summary>
    /// The adapter's ATI identity, or null when nothing usable is on the far end.
    /// </summary>
    /// <remarks>
    /// One exchange, no ATZ. Unlike serial there is no wrong-baud-rate garbage to clear, and the
    /// prompt is proof enough on its own: this only returns when the far end terminated its reply
    /// with '&gt;', which nothing else on a home network does. Skipping the reset also keeps the probe
    /// to a couple of seconds per candidate rather than six.
    /// </remarks>
    async Task<string?> ProbeAdapter(CancellationToken ct)
    {
        try
        {
            var id = await this.Exchange("ATI\r", this.config.ProbeTimeout, ct).ConfigureAwait(false);
            return String.IsNullOrWhiteSpace(id) ? null : id;
        }
        catch (ObdTimeoutException)
        {
            return null;
        }
        catch (ObdException)
        {
            return null;
        }
    }

    /// <summary>
    /// One request/response round trip. Callers hold <see cref="sendLock"/>.
    /// </summary>
    async Task<string> Exchange(string command, TimeSpan timeout, CancellationToken ct)
    {
        var active = this.stream ?? throw new ObdException("Not connected to OBD adapter");

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

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            try
            {
                return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Our own deadline elapsed, not the caller's token. Report it as what it is so a
                // polling caller doesn't mistake a quiet adapter for its own shutdown.
                throw new ObdTimeoutException(command.Trim(), timeout);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            this.MarkLinkDown();
            throw new ObdException("The WiFi connection to the OBD adapter was lost", ex);
        }
        finally
        {
            // However this ended, the exchange is over. Clearing it before releasing the lock is what
            // stops a late reply to a timed-out command from completing the *next* command's wait
            // with the previous command's data - one timeout would otherwise put every response
            // after it off by one, and the session would never recover on its own.
            this.EndExchange();
            Interlocked.Exchange(ref this.lastActivity, Environment.TickCount64);
        }
    }

    async Task Open(WifiObdEndpoint endpoint, CancellationToken ct)
    {
        var sock = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = this.config.NoDelay };
        this.config.ConfigureSocket?.Invoke(sock);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(this.config.ConnectTimeout);

            await sock.ConnectAsync(endpoint.Host, endpoint.Port, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sock.Dispose();
            throw new ObdException(
                $"Timed out connecting to {endpoint}. Nothing answered, which usually means this device is not joined to the adapter's WiFi network - or, on Android, that traffic is still being routed to cellular because the adapter's network has no internet."
            );
        }
        catch (SocketException ex)
        {
            sock.Dispose();
            throw new ObdException(Explain(endpoint, ex), ex);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            sock.Dispose();
            throw new ObdException($"'{endpoint.Host}' is not a usable host name or address", ex);
        }

        this.socket = sock;
        this.stream = new NetworkStream(sock, ownsSocket: false);
        this.ConnectedEndpoint = endpoint;
        this.linkUp = true;
        Interlocked.Exchange(ref this.lastActivity, Environment.TickCount64);

        this.StartReadPump();
        this.DiscardStale();
    }

    /// <summary>
    /// Turns a socket error into the sentence that fixes it.
    /// </summary>
    static string Explain(WifiObdEndpoint endpoint, SocketException ex) => ex.SocketErrorCode switch
    {
        // Something is at that address but nothing is listening on that port. Almost always the
        // 35000-vs-23 split, or an adapter that has not finished booting.
        SocketError.ConnectionRefused =>
            $"Nothing is listening on {endpoint}. The host is reachable, so this is the wrong port - ELM327 WiFi adapters use 35000, a few clones use 23.",

        SocketError.HostUnreachable or SocketError.NetworkUnreachable or SocketError.NetworkDown =>
            $"No route to {endpoint}. Join the adapter's WiFi network first; on Android also bind to that network (ConnectivityManager.BindProcessToNetwork), because it has no internet and the OS will otherwise keep routing to cellular.",

        SocketError.AccessDenied =>
            $"Access denied connecting to {endpoint}. On iOS this is the local network permission - add NSLocalNetworkUsageDescription to Info.plist; a denial is otherwise silent.",

        _ => $"Could not connect to {endpoint} ({ex.SocketErrorCode})"
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
    /// Continuously drains the socket into the current exchange's buffer.
    /// </summary>
    /// <remarks>
    /// A pump rather than a read-per-command because an ELM327 does not answer in one write, and TCP
    /// gives no reason to expect one read per write either - a multi-frame CAN reply arrives as
    /// several segments, and the adapter also emits unsolicited text (the reset banner,
    /// "SEARCHING..."). Reading continuously and completing on the '&gt;' prompt is what makes those
    /// cases fall out naturally.
    /// </remarks>
    async Task ReadPump(NetworkStream ns, CancellationToken ct)
    {
        var buffer = new byte[512];

        while (!ct.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await ns.ReadAsync(buffer, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidOperationException)
            {
                // The AP went out of range, or the socket was closed under us. Fail whoever is
                // waiting rather than letting them sit out the full command timeout.
                this.MarkLinkDown(new ObdException("The WiFi connection to the OBD adapter was lost", ex));
                return;
            }

            if (read <= 0)
            {
                // An orderly close from the adapter. Far more common here than on a serial port:
                // clone firmware drops a socket that has been idle for a minute or so, which is why
                // KeepAliveInterval exists.
                this.MarkLinkDown(new ObdException("The OBD adapter closed the WiFi connection"));
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

    void StartKeepAlive()
    {
        if (this.config.KeepAliveInterval <= TimeSpan.Zero)
            return;

        this.keepAliveCts = new CancellationTokenSource();
        var token = this.keepAliveCts.Token;
        _ = Task.Run(() => this.KeepAlive(token), CancellationToken.None);
    }

    void StopKeepAlive()
    {
        try
        {
            this.keepAliveCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        this.keepAliveCts?.Dispose();
        this.keepAliveCts = null;
    }

    /// <summary>
    /// Sends a cheap ATI when the link has gone quiet, so the adapter does not drop it.
    /// </summary>
    /// <remarks>
    /// ATI is answered by the adapter itself and never touches the vehicle bus, so this cannot
    /// disturb a reading or wake an ECU. It also skips itself whenever a real command is in flight -
    /// that traffic is the keep-alive.
    /// </remarks>
    async Task KeepAlive(CancellationToken ct)
    {
        var interval = this.config.KeepAliveInterval;
        var tick = TimeSpan.FromMilliseconds(Math.Max(1000, interval.TotalMilliseconds / 2));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(tick, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!this.IsConnected)
                return;

            var idle = Environment.TickCount64 - Interlocked.Read(ref this.lastActivity);
            if (idle < interval.TotalMilliseconds)
                continue;

            if (!await this.sendLock.WaitAsync(0, ct).ConfigureAwait(false))
                continue;

            try
            {
                await this.Exchange("ATI\r", this.config.ProbeTimeout, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Nothing to do about it here. The link is already marked down if it died, and the
                // caller finds out on its next command rather than from a background task.
                this.logger.LogDebug(ex, "Keep-alive failed");
            }
            finally
            {
                this.sendLock.Release();
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

    void CloseSocket()
    {
        var ns = this.stream;
        var sock = this.socket;

        this.stream = null;
        this.socket = null;
        this.ConnectedEndpoint = null;
        this.linkUp = false;

        try
        {
            ns?.Dispose();

            if (sock != null)
            {
                if (sock.Connected)
                    sock.Shutdown(SocketShutdown.Both);

                sock.Dispose();
            }
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException or IOException)
        {
            // Shutting down a socket whose peer has already vanished throws on some platforms.
            this.logger.LogDebug(ex, "Error closing WiFi socket");
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

    static WifiObdEndpoint Endpoint(ObdDiscoveredDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (device.NativeDevice is WifiObdEndpoint endpoint)
            return endpoint;

        throw new ObdException(
            $"'{device.Name}' did not come from a WifiObdDeviceScanner - its NativeDevice is {device.NativeDevice?.GetType().Name ?? "null"}, not a WifiObdEndpoint."
        );
    }

    static WifiObdConfiguration Clone(WifiObdConfiguration source, WifiObdEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new WifiObdConfiguration
        {
            Host = endpoint.Host,
            Port = endpoint.Port,

            // The endpoint came from a scan that already got an ATI out of it, so re-probing it - and
            // worse, falling through to the other candidates when this one is momentarily busy - would
            // only obscure what went wrong.
            AutoDetectEndpoint = false,
            IncludeGatewayCandidates = false,
            EndpointCandidates = source.EndpointCandidates,
            ConnectTimeout = source.ConnectTimeout,
            ProbeTimeout = source.ProbeTimeout,
            CommandTimeout = source.CommandTimeout,
            KeepAliveInterval = source.KeepAliveInterval,
            NoDelay = source.NoDelay,
            ConfigureSocket = source.ConfigureSocket
        };
    }
}
