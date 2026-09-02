using System;
using System.Threading;
using System.Threading.Tasks;

namespace Shiny.Obd;

/// <summary>
/// Standard ELM327 initialization profile.
/// Works with genuine ELM327 chips and most clones.
/// </summary>
/// <param name="protocol">
/// The ELM protocol number to pin with <c>ATSP</c>, as reported by
/// <see cref="ObdConnection.NegotiatedProtocol"/> on an earlier session. Null lets the adapter search
/// for one itself.
/// </param>
public class Elm327AdapterProfile(string? protocol = null) : IObdAdapterProfile
{
    /// <summary>
    /// How long the chip is given to come back up after a reset. An ELM327 prints its prompt before it
    /// has finished resetting, so the reply is not the signal that it is ready.
    /// </summary>
    protected static readonly TimeSpan ResetDelay = TimeSpan.FromSeconds(1);

    public string Name => "ELM327";

    /// <summary>The protocol this profile pins, or null when it lets the adapter search for one.</summary>
    public string? Protocol { get; } = protocol;

    public virtual async Task Initialize(IObdConnection connection, CancellationToken ct = default)
    {
        // The one and only reset of a connect. ObdConnection's auto-detect probe used to send an ATZ of
        // its own before this one, so every connection reset the adapter twice, waited out ResetDelay
        // twice, and threw away everything the first sequence had established.
        await connection.SendRaw("ATZ", ct).ConfigureAwait(false);     // Reset
        await Task.Delay(ResetDelay, ct).ConfigureAwait(false);        // Wait for reset
        await connection.SendRaw("ATE0", ct).ConfigureAwait(false);    // Echo off
        await connection.SendRaw("ATL0", ct).ConfigureAwait(false);    // Linefeed off
        await connection.SendRaw("ATS1", ct).ConfigureAwait(false);    // Spaces on
        await connection.SendRaw("ATH0", ct).ConfigureAwait(false);    // Headers off
        await this.SelectProtocol(connection, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Puts the adapter on a protocol, pinning the one an earlier session negotiated where there is one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ATSP0</c> does not choose a protocol — it defers the choice to the first command that needs
    /// the bus, and that command then pays the whole ELM search. It is seconds of it, routinely longer
    /// than a command timeout, and <c>ATZ</c> discards the result, so an unpinned adapter pays it again
    /// on every reconnect. Handing back the number from last time skips the search outright, which is
    /// most of what makes a reconnect quick.
    /// </para>
    /// <para>
    /// ⚠️ A pin can be wrong — a different vehicle, or a number learned on a bad session — and a wrong
    /// protocol simply never answers. So a pin is verified with mode 01 and dropped for a search when
    /// nothing comes back. The same probe failing on an <i>unpinned</i> adapter is not evidence of
    /// anything: the bus is asleep with the ignition off, which is a perfectly normal state to connect
    /// in, and failing the connect for it would be wrong.
    /// </para>
    /// </remarks>
    protected async Task SelectProtocol(IObdConnection connection, CancellationToken ct)
    {
        if (this.Protocol == null)
        {
            await connection.SendRaw("ATSP0", ct).ConfigureAwait(false);
            return;
        }

        await connection.SendRaw("ATSP" + this.Protocol, ct).ConfigureAwait(false);

        if (!await Probe(connection, ct).ConfigureAwait(false))
            await connection.SendRaw("ATSP0", ct).ConfigureAwait(false);
    }

    /// <summary>Whether the vehicle answers mode 01 on the protocol currently selected.</summary>
    /// <remarks>
    /// Matched on the mode echo with spacing removed, because <c>ATS1</c> is a request and clones
    /// ignore it. Anything else the adapter can say here — <c>NO DATA</c>, <c>UNABLE TO CONNECT</c>, a
    /// timeout — is a protocol that is not answering.
    /// </remarks>
    static async Task<bool> Probe(IObdConnection connection, CancellationToken ct)
    {
        try
        {
            var response = await connection.SendRaw("0100", ct).ConfigureAwait(false);
            return response
                .Replace(" ", "")
                .Contains("4100", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // A timeout or a transport error is exactly what a wrong pin looks like from here
            return false;
        }
    }
}
