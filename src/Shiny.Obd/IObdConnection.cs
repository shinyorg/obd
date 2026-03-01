using System;
using System.Threading;
using System.Threading.Tasks;

namespace Shiny.Obd;

/// <summary>
/// Represents a connection to a vehicle's OBD-II system through an ELM327-compatible adapter.
/// </summary>
public interface IObdConnection : IAsyncDisposable
{
    bool IsConnected { get; }

    /// <summary>
    /// Connect to the OBD adapter and initialize the ELM327 protocol
    /// </summary>
    Task Connect(CancellationToken ct = default);

    /// <summary>
    /// Disconnect from the OBD adapter
    /// </summary>
    Task Disconnect();

    /// <summary>
    /// Execute a typed OBD command and return the parsed result
    /// </summary>
    Task<T> Execute<T>(IObdCommand<T> command, CancellationToken ct = default);

    /// <summary>
    /// Send a raw command string (e.g. AT commands) and return the raw response
    /// </summary>
    Task<string> SendRaw(string command, CancellationToken ct = default);
}
