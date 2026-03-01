using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shiny.BluetoothLE;

namespace Shiny.Obd.Ble;

/// <summary>
/// Scans for BLE OBD adapter devices using Shiny.BluetoothLE.
/// </summary>
public class BleObdDeviceScanner : IObdDeviceScanner
{
    readonly IBleManager bleManager;
    readonly BleObdConfiguration config;

    public BleObdDeviceScanner(IBleManager bleManager, BleObdConfiguration? config = null)
    {
        this.bleManager = bleManager ?? throw new ArgumentNullException(nameof(bleManager));
        this.config = config ?? new BleObdConfiguration();
    }

    public Task Scan(Action<ObdDiscoveredDevice> onDeviceFound, CancellationToken ct = default)
    {
        var seen = new HashSet<string>();
        var tcs = new TaskCompletionSource<bool>();

        ct.Register(() => tcs.TrySetResult(true));

        this.bleManager
            .Scan()
            .Where(x =>
                !string.IsNullOrEmpty(x.Peripheral.Name) &&
                (this.config.DeviceNameFilter == null ||
                 x.Peripheral.Name.Contains(this.config.DeviceNameFilter, StringComparison.OrdinalIgnoreCase)))
            .Subscribe(
                x =>
                {
                    var id = x.Peripheral.Uuid;
                    if (seen.Add(id))
                    {
                        var device = new ObdDiscoveredDevice(
                            x.Peripheral.Name ?? "Unknown",
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
}
