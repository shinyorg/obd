using System;

namespace Shiny.Obd;

/// <summary>
/// The adapter did not answer a command within the transport's configured timeout.
/// </summary>
/// <remarks>
/// Deliberately not an <see cref="OperationCanceledException"/>. A caller polling in a loop has to be
/// able to tell "the adapter went quiet for a moment" apart from "my own cancellation token fired",
/// and if a transport reports its own deadline as a cancellation the two are indistinguishable — a
/// single slow reply then looks exactly like a shutdown request.
/// </remarks>
public class ObdTimeoutException : ObdException
{
    public ObdTimeoutException(string command, TimeSpan timeout)
        : base($"The OBD adapter did not respond to '{command}' within {timeout.TotalSeconds:0.##}s")
    {
        this.Command = command;
        this.Timeout = timeout;
    }

    /// <summary>
    /// The command that went unanswered
    /// </summary>
    public string Command { get; }

    /// <summary>
    /// The timeout that elapsed waiting for it
    /// </summary>
    public TimeSpan Timeout { get; }
}
