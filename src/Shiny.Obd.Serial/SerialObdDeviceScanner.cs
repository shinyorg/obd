using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Shiny.Obd.Serial;

/// <summary>
/// Enumerates serial ports that could host an OBD adapter.
/// </summary>
/// <remarks>
/// Unlike BLE scanning this is a directory read, not a radio operation - it completes immediately
/// and costs nothing. It is still surfaced through <see cref="IObdDeviceScanner"/> so that a caller
/// can offer "pick your adapter" over any transport without caring which one it is.
///
/// <see cref="ObdDiscoveredDevice.NativeDevice"/> is the <see cref="SerialPortInfo"/>, and
/// <see cref="ObdDiscoveredDevice.Id"/> is the port path to open.
/// </remarks>
public class SerialObdDeviceScanner : IObdDeviceScanner
{
    readonly SerialObdConfiguration config;
    readonly ILogger logger;

    public SerialObdDeviceScanner(
        SerialObdConfiguration? config = null,
        ILogger<SerialObdDeviceScanner>? logger = null
    )
    {
        this.config = config ?? new SerialObdConfiguration();
        this.logger = logger ?? NullLogger<SerialObdDeviceScanner>.Instance;
    }

    public async Task Scan(Action<ObdDiscoveredDevice> onDeviceFound, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(onDeviceFound);

        // Ports appear and disappear as things are plugged in, so this re-reads on a slow interval
        // rather than returning once - it matches how a BLE scan behaves and lets a UI stay live.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (!ct.IsCancellationRequested)
        {
            foreach (var portInfo in SerialPortEnumerator.Discover())
            {
                if (this.config.PortNameFilter != null &&
                    !portInfo.Description.Contains(this.config.PortNameFilter, StringComparison.OrdinalIgnoreCase) &&
                    !portInfo.PortName.Contains(this.config.PortNameFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var id = portInfo.StablePath ?? portInfo.PortName;
                if (!seen.Add(id))
                    continue;

                this.logger.LogDebug("Discovered serial port {Port}", portInfo);
                onDeviceFound(new ObdDiscoveredDevice(portInfo.Description, id, portInfo));
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
