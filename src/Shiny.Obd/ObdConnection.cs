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
public class ObdConnection : IObdConnection
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

    public async Task Connect(CancellationToken ct = default)
    {
        await this.transport.Connect(ct).ConfigureAwait(false);

        if (this.profile != null)
        {
            await this.profile.Initialize(this, ct).ConfigureAwait(false);
            return;
        }

        // Auto-detect: reset first, then probe with ATI
        await this.SendRaw("ATZ", ct).ConfigureAwait(false);
        await Task.Delay(1000, ct).ConfigureAwait(false);

        var atiResponse = await this.SendRaw("ATI", ct).ConfigureAwait(false);
        this.DetectedAdapter = ParseAdapterInfo(atiResponse);

        var resolved = this.DetectedAdapter.Type switch
        {
            ObdAdapterType.ObdLink => (IObdAdapterProfile)new ObdLinkAdapterProfile(),
            _ => new Elm327AdapterProfile()
        };
        await resolved.Initialize(this, ct).ConfigureAwait(false);
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

        var hexBytes = new List<byte>();
        foreach (var line in lines)
        {
            // Handle CAN multi-frame "N: XX XX XX" format
            var hexPart = line.Contains(":")
                ? line.Substring(line.IndexOf(':') + 1).Trim()
                : line;

            var parts = hexPart.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (byte.TryParse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                    hexBytes.Add(b);
            }
        }

        return hexBytes.ToArray();
    }
}
