using System;
using System.Threading;
using System.Threading.Tasks;

namespace Shiny.Obd;

/// <summary>
/// Abstraction over the communication channel to an ELM327-compatible OBD adapter.
/// Implementations handle the raw send/receive mechanics (BLE, WiFi, USB, etc.)
/// </summary>
public interface IObdTransport : IAsyncDisposable
{
    bool IsConnected { get; }

    /// <summary>
    /// Establish the transport connection
    /// </summary>
    Task Connect(CancellationToken ct = default);

    /// <summary>
    /// Close the transport connection
    /// </summary>
    Task Disconnect();

    /// <summary>
    /// Send a command string and wait for the complete response (up to the '>' prompt)
    /// </summary>
    Task<string> Send(string command, CancellationToken ct = default);
}
