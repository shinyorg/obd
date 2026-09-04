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
        //
        // Nameless advertisements are surfaced too, and dropping them was a real bug. The name here is
        // `Peripheral.Name ?? AdvertisementData.LocalName`, and on iOS `CBPeripheral.Name` stays null
        // until CoreBluetooth has connected to that peripheral once and cached it - so a requirement for
        // a name is in practice a *first-connection-of-the-process* filter on that platform. An adapter
        // advertising from the OBD port was invisible on exactly the attempt that mattered, and became
        // visible only once a connection had already succeeded by some other route. Plenty of ELM327
        // clones also carry no local name in the advertisement at all and only ever report one after
        // connecting. Callers that identify an adapter by its peripheral id - the only key always
        // present - could not do so through this scanner.
        //
        // An explicit DeviceNameFilter still excludes them, because Matches cannot match a name that
        // isn't there. That is the caller asking for a name; requiring one unconditionally was not.
        this.bleManager
            .Scan()
            .Select(BleScanCandidate.From)
            .Do(this.LogCandidate)
            .Where(x => x.Matches(this.config.DeviceNameFilter))
            .Subscribe(
                x =>
                {
                    var id = x.Peripheral.Uuid;
                    if (seen.Add(id))
                    {
                        var device = new ObdDiscoveredDevice(
                            // Empty rather than null: ObdDiscoveredDevice.Name is non-nullable, and a
                            // picker showing a blank row the user can still select by signal strength is
                            // worth more than an adapter it never lists at all.
                            x.Name ?? string.Empty,
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
