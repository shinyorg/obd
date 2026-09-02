using System.Threading;
using System.Threading.Tasks;

namespace Shiny.Obd;

/// <summary>
/// OBDLink (STN1110/STN2120) initialization profile.
/// Includes standard ELM327 commands plus STN-specific optimizations.
/// </summary>
/// <param name="protocol">
/// The ELM protocol number to pin with <c>ATSP</c>, as reported by
/// <see cref="ObdConnection.NegotiatedProtocol"/> on an earlier session.
/// </param>
public class ObdLinkAdapterProfile(string? protocol = null) : Elm327AdapterProfile(protocol)
{
    public new string Name => "OBDLink";

    public override async Task Initialize(IObdConnection connection, CancellationToken ct = default)
    {
        // ⚠️ STFAC restores factory defaults, so it has to run *before* the ELM327 configuration rather
        // than after it. Sending it last — which is what this did — wiped ATE0/ATL0/ATS1/ATH0 and the
        // protocol selection in the same breath as setting them, so every OBDLink session ran with echo
        // on, no spacing and an unpinned protocol. The ATZ that opens the base sequence covers the
        // settle this reset needs, so there is no second delay here.
        await connection.SendRaw("STFAC", ct).ConfigureAwait(false);   // Reset to factory defaults

        await base.Initialize(connection, ct).ConfigureAwait(false);

        // STN-specific optimizations
        await connection.SendRaw("ATCAF1", ct).ConfigureAwait(false);  // CAN auto formatting on
    }
}
