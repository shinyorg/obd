using System;
using System.Threading;
using System.Threading.Tasks;

namespace Shiny.Obd;

/// <summary>
/// Scans for available OBD adapter devices. 
/// Applicable to transports that support discovery (BLE, WiFi).
/// Cancel the token to stop scanning.
/// </summary>
public interface IObdDeviceScanner
{
    /// <summary>
    /// Scan for OBD adapter devices. Each discovered device invokes the callback.
    /// The task completes when the cancellation token is cancelled.
    /// </summary>
    Task Scan(Action<ObdDiscoveredDevice> onDeviceFound, CancellationToken ct = default);
}
