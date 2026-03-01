using System.Threading;
using System.Threading.Tasks;

namespace Shiny.Obd;

/// <summary>
/// Defines the initialization sequence for a specific OBD adapter type.
/// </summary>
public interface IObdAdapterProfile
{
    string Name { get; }

    /// <summary>
    /// Send the initialization AT commands for this adapter
    /// </summary>
    Task Initialize(IObdConnection connection, CancellationToken ct = default);
}
