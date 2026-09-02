using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Shiny.Obd;

/// <summary>
/// ELM327-based OBD connection. Handles initialization, command execution,
/// and response parsing over any <see cref="IObdTransport"/>.
/// </summary>
/// <remarks>
/// Implements <see cref="IDisposable"/> as well as the interface's <see cref="IAsyncDisposable"/>.
/// That is not redundancy: a DI container checks the *concrete* type for <c>IDisposable</c> when a
/// container-owned singleton is torn down, and throws
/// <c>InvalidOperationException: type only implements IAsyncDisposable</c> if it finds none on the
/// synchronous path. Hosts dispose their provider asynchronously, but a console app or a test doing
/// <c>using var provider = services.BuildServiceProvider()</c> does not — and would fail on exit
/// rather than anywhere near the cause.
/// </remarks>
public class ObdConnection : IObdConnection, IDisposable
{
    readonly IObdTransport transport;
    readonly IObdAdapterProfile? profile;

    /// <summary>
    /// Creates a connection that auto-detects the adapter and uses the appropriate profile
    /// </summary>
    public ObdConnection(IObdTransport transport)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    /// <summary>
    /// Creates a connection with an explicit adapter profile (skips auto-detection)
    /// </summary>
    public ObdConnection(IObdTransport transport, IObdAdapterProfile profile)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    public bool IsConnected => this.transport.IsConnected;

    /// <summary>
    /// Adapter info detected during Connect. Null if profile was provided explicitly.
    /// </summary>
    public ObdAdapterInfo? DetectedAdapter { get; private set; }

    /// <summary>
    /// The ELM protocol number to pin on the next <see cref="Connect"/>, from an earlier session's
    /// <see cref="NegotiatedProtocol"/>. Null lets the adapter search for one.
    /// </summary>
    /// <remarks>
    /// Persist this between sessions and hand it back: the search it skips is the single most expensive
    /// part of establishing a session, and it is otherwise paid again on every reconnect. It is ignored
    /// when an explicit profile was supplied — that profile carries its own.
    /// </remarks>
    public string? Protocol { get; set; }

    /// <summary>
    /// The protocol the adapter reports it is actually on, read once initialization is complete. Null
    /// when the adapter has not settled on one — which is what an unpinned adapter says until something
    /// has needed the bus.
    /// </summary>
    public string? NegotiatedProtocol { get; private set; }

    public async Task Connect(CancellationToken ct = default)
    {
        await this.transport.Connect(ct).ConfigureAwait(false);
        this.NegotiatedProtocol = null;

        if (this.profile != null)
        {
            await this.profile.Initialize(this, ct).ConfigureAwait(false);
        }
        else
        {
            // Auto-detect with ATI alone. ATI answers whatever state the chip is in, and the profile's
            // own ATZ is a few commands away — resetting here as well was a second full adapter reset
            // and a second one-second settle on every connect, for nothing.
            var atiResponse = await this.SendRaw("ATI", ct).ConfigureAwait(false);
            this.DetectedAdapter = ParseAdapterInfo(atiResponse);

            var resolved = this.DetectedAdapter.Type switch
            {
                ObdAdapterType.ObdLink => (IObdAdapterProfile)new ObdLinkAdapterProfile(this.Protocol),
                _ => new Elm327AdapterProfile(this.Protocol)
            };
            await resolved.Initialize(this, ct).ConfigureAwait(false);
        }

        await this.RefreshNegotiatedProtocol(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Asks the adapter which protocol it ended up on, so the caller can hand it back next time, and
    /// updates <see cref="NegotiatedProtocol"/> with the answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ Worth calling again once real traffic has been through. <see cref="Connect"/> asks as it
    /// finishes, but an adapter left to search (no <see cref="Protocol"/> pinned) has not chosen
    /// anything at that point — <c>ATSP0</c> defers the choice to the first command that needs the
    /// bus — so it answers "still searching" and this reports null. A caller that reads a PID and then
    /// asks again gets the real number, which is the one worth persisting.
    /// </para>
    /// ATDPN answers a bare digit for a protocol that was set explicitly and an <c>A</c>-prefixed one
    /// for a protocol it found by searching. Both name the same protocol, so the prefix is dropped —
    /// but only from a two-character reply, because <c>A</c> is itself a protocol number (J1939) and
    /// stripping it off a bare "A" would leave nothing. <c>0</c> means it has not determined one yet;
    /// there is nothing to remember, so that reports null rather than a number that would pin the
    /// search itself.
    /// </remarks>
    public async Task<string?> RefreshNegotiatedProtocol(CancellationToken ct = default)
    {
        this.NegotiatedProtocol = await this.ReadProtocolNumber(ct).ConfigureAwait(false);
        return this.NegotiatedProtocol;
    }

    async Task<string?> ReadProtocolNumber(CancellationToken ct)
    {
        try
        {
            var raw = await this.SendRaw("ATDPN", ct).ConfigureAwait(false);

            // Last non-empty line: an adapter still echoing prints the command back first
            var lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
                return null;

            var number = lines[lines.Length - 1].Trim();
            if (number.Length == 2 && (number[0] == 'A' || number[0] == 'a'))
                number = number.Substring(1);

            return number.Length == 1 && number != "0" && Uri.IsHexDigit(number[0])
                ? number.ToUpperInvariant()
                : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // A working session is not worth failing over an adapter that won't say what it is doing
            return null;
        }
    }

    public Task Disconnect() => this.transport.Disconnect();

    public async Task<T> Execute<T>(IObdCommand<T> command, CancellationToken ct = default)
    {
        var rawResponse = await this.SendRaw(command.RawCommand, ct).ConfigureAwait(false);
        ValidateResponse(rawResponse);
        var bytes = ParseHexResponse(rawResponse);
        return command.Parse(bytes);
    }

    public async Task<string> SendRaw(string command, CancellationToken ct = default)
    {
        return await this.transport.Send(command + "\r", ct).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => this.transport.DisposeAsync();

    public void Dispose()
    {
        // Prefer a transport's own synchronous teardown when it has one. The built-in transports do,
        // and their DisposeAsync completes synchronously anyway.
        if (this.transport is IDisposable sync)
        {
            sync.Dispose();
        }
        else
        {
            // A third-party transport that is genuinely async. Blocking is the honest option here —
            // the alternative is abandoning the teardown, which leaks the underlying handle.
            var pending = this.transport.DisposeAsync();
            if (pending.IsCompleted)
                pending.GetAwaiter().GetResult();
            else
                pending.AsTask().GetAwaiter().GetResult();
        }

        GC.SuppressFinalize(this);
    }

    static ObdAdapterInfo ParseAdapterInfo(string atiResponse)
    {
        var trimmed = atiResponse.Trim();

        if (trimmed.Contains("STN", StringComparison.OrdinalIgnoreCase))
            return new ObdAdapterInfo(trimmed, ObdAdapterType.ObdLink);

        if (trimmed.Contains("ELM327", StringComparison.OrdinalIgnoreCase))
            return new ObdAdapterInfo(trimmed, ObdAdapterType.Elm327);

        return new ObdAdapterInfo(trimmed, ObdAdapterType.Unknown);
    }

    static void ValidateResponse(string response)
    {
        var trimmed = response.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ObdException("Empty response received");

        if (trimmed.Contains("NO DATA"))
            throw new ObdException("No data received from vehicle");

        if (trimmed.Contains("UNABLE TO CONNECT"))
            throw new ObdException("Unable to connect to vehicle");

        if (trimmed.Contains("BUS INIT: ...ERROR"))
            throw new ObdException("Bus initialization error");

        if (trimmed == "?")
            throw new ObdException("Unknown command");
    }

    static byte[] ParseHexResponse(string response)
    {
        var lines = response
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .Where(l => !l.StartsWith("SEARCHING", StringComparison.OrdinalIgnoreCase))
            .Where(l => !l.StartsWith("BUS INIT", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // An ELM327 prefixes a multi-frame CAN response with a bare byte-count line and numbers
        // each frame that follows:
        //
        //     014
        //     0: 49 02 01 57 42 41
        //     1: 31 32 33 34 35 36 37
        //     2: 38 39 30 31 32 33 34
        //
        // That count is framing, not payload — and "014" parses cleanly as 0x14, so leaving it in
        // shifts every byte of the response along by one and the mode echo check then rejects a
        // perfectly good reply. Single-frame responses carry no such line, which is why only the
        // multi-frame commands (mode 09 VIN, mode 03 with three or more codes) were affected.
        var numbered = lines.Where(HasFrameIndex).ToList();
        var payloadLines = numbered.Count > 0 ? numbered : lines;

        var hexBytes = new List<byte>();
        foreach (var line in payloadLines)
        {
            var colon = line.IndexOf(':');
            var hexPart = colon >= 0 ? line.Substring(colon + 1).Trim() : line;

            foreach (var b in ParseHexBytes(hexPart))
                hexBytes.Add(b);
        }

        return hexBytes.ToArray();
    }

    /// <summary>
    /// Whether the line is a numbered frame of a multi-frame response ("0: 49 02 ...").
    /// </summary>
    /// <remarks>
    /// The index is hex and runs 0-F before wrapping, so it is one character in practice — but it is
    /// matched as "all hex digits" rather than by length so a longer index cannot silently be read as
    /// payload. Requiring <c>idx &gt; 0</c> keeps a leading-colon line from qualifying with an empty index.
    /// </remarks>
    static bool HasFrameIndex(string line)
    {
        var idx = line.IndexOf(':');
        if (idx <= 0)
            return false;

        for (var i = 0; i < idx; i++)
        {
            if (!Uri.IsHexDigit(line[i]))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Reads a line of hex, whether or not the adapter is spacing it.
    /// </summary>
    /// <remarks>
    /// The profiles ask for spaces (ATS1), but clones ignore it and an unspaced run would otherwise
    /// fail to parse as a single token and be dropped in full — losing the whole response rather than
    /// reporting a problem.
    /// </remarks>
    static IEnumerable<byte> ParseHexBytes(string hex)
    {
        var parts = hex.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            // Split on length rather than on whether the whole token parses: "0012" fits in a byte
            // as 0x12, so a parse-first rule would read two bytes as one and drop the leading zero.
            // A hex byte is two characters, so anything longer is a run of them; an odd length is
            // not a byte boundary at all and is left alone rather than guessed at.
            if (part.Length <= 2)
            {
                if (byte.TryParse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var single))
                    yield return single;
            }
            else if (part.Length % 2 == 0)
            {
                for (var i = 0; i < part.Length; i += 2)
                {
                    if (byte.TryParse(part.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                        yield return b;
                }
            }
        }
    }
}
