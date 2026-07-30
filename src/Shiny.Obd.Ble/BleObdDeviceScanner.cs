using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shiny.BluetoothLE;

namespace Shiny.Obd.Ble;

/// <summary>
/// Scans for BLE OBD adapter devices using Shiny.BluetoothLE.
/// </summary>
public class BleObdDeviceScanner : IObdDeviceScanner
{
    readonly IBleManager bleManager;
    readonly BleObdConfiguration config;
    readonly ILogger logger;

    public BleObdDeviceScanner(
        IBleManager bleManager,
        BleObdConfiguration? config = null,
        ILogger<BleObdDeviceScanner>? logger = null
    )
    {
        this.bleManager = bleManager ?? throw new ArgumentNullException(nameof(bleManager));
        this.config = config ?? new BleObdConfiguration();
        this.logger = logger ?? NullLogger<BleObdDeviceScanner>.Instance;
    }

    public Task Scan(Action<ObdDiscoveredDevice> onDeviceFound, CancellationToken ct = default)
    {
        var seen = new HashSet<string>();
        var tcs = new TaskCompletionSource<bool>();

        ct.Register(() => tcs.TrySetResult(true));

        // The scan is deliberately unfiltered. Filtering on the adapter's service UUID looks like the
        // obvious optimization, but iOS matches that filter against the *advertisement* only, and most
        // ELM327 clones don't advertise their service - it only shows up after connecting. A filtered
        // scan would find nothing at all on iPhone.
        this.bleManager
            .Scan()
            .Select(BleScanCandidate.From)
            .Do(this.LogCandidate)
            .Where(x => !string.IsNullOrEmpty(x.Name) && x.Matches(this.config.DeviceNameFilter))
            .Subscribe(
                x =>
                {
                    var id = x.Peripheral.Uuid;
                    if (seen.Add(id))
                    {
                        var device = new ObdDiscoveredDevice(
                            x.Name!,
                            id,
                            x.Peripheral
                        );
                        onDeviceFound(device);
                    }
                },
                ex => tcs.TrySetException(ex),
                () => tcs.TrySetResult(true),
                ct
            );

        return tcs.Task;
    }

    /// <summary>
    /// Dumps every advertisement seen, before any filtering, so a device that never reaches the
    /// callback can still be identified - and so the UUIDs it actually advertises are visible when the
    /// configured service/characteristic UUIDs turn out to be wrong for that adapter.
    /// </summary>
    void LogCandidate(BleScanCandidate candidate)
    {
        if (!this.logger.IsEnabled(LogLevel.Debug))
            return;

        // Peripheral.Name is logged alongside the resolved name on purpose: when it reads "(null)" and
        // Name doesn't, the name came from the advertisement and this device would have been dropped
        // by a Peripheral.Name-only filter.
        this.logger.LogDebug(
            "BLE advertisement - Name: {Name}, Peripheral.Name: {PeripheralName}, Id: {Id}, RSSI: {Rssi}, Services: {ServiceUuids}",
            candidate.Name ?? "(none)",
            candidate.Peripheral.Name ?? "(null)",
            candidate.Peripheral.Uuid,
            candidate.Rssi,
            candidate.ServiceUuids is { Length: > 0 }
                ? string.Join(", ", candidate.ServiceUuids)
                : "(none advertised)"
        );
    }
}
