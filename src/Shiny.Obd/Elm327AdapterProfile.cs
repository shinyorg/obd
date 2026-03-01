using System.Threading;
using System.Threading.Tasks;

namespace Shiny.Obd;

/// <summary>
/// Standard ELM327 initialization profile.
/// Works with genuine ELM327 chips and most clones.
/// </summary>
public class Elm327AdapterProfile : IObdAdapterProfile
{
    public string Name => "ELM327";

    public virtual async Task Initialize(IObdConnection connection, CancellationToken ct = default)
    {
        await connection.SendRaw("ATZ", ct).ConfigureAwait(false);     // Reset
        await Task.Delay(1000, ct).ConfigureAwait(false);              // Wait for reset
        await connection.SendRaw("ATE0", ct).ConfigureAwait(false);    // Echo off
        await connection.SendRaw("ATL0", ct).ConfigureAwait(false);    // Linefeed off
        await connection.SendRaw("ATS1", ct).ConfigureAwait(false);    // Spaces on
        await connection.SendRaw("ATH0", ct).ConfigureAwait(false);    // Headers off
        await connection.SendRaw("ATSP0", ct).ConfigureAwait(false);   // Auto protocol
    }
}
