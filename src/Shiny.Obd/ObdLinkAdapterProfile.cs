using System.Threading;
using System.Threading.Tasks;

namespace Shiny.Obd;

/// <summary>
/// OBDLink (STN1110/STN2120) initialization profile.
/// Includes standard ELM327 commands plus STN-specific optimizations.
/// </summary>
public class ObdLinkAdapterProfile : Elm327AdapterProfile
{
    public new string Name => "OBDLink";

    public override async Task Initialize(IObdConnection connection, CancellationToken ct = default)
    {
        await base.Initialize(connection, ct).ConfigureAwait(false);

        // STN-specific optimizations
        await connection.SendRaw("STFAC", ct).ConfigureAwait(false);   // Reset to factory defaults
        await connection.SendRaw("ATCAF1", ct).ConfigureAwait(false);  // CAN auto formatting on
    }
}
