using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shiny.Bluetooth;

namespace Shiny.Obd.Bluetooth;

/// <summary>
/// Finds classic Bluetooth OBD adapters - the paired ones immediately, then unpaired ones by inquiry.
/// </summary>
/// <remarks>
/// Paired devices are reported first because they are what a user almost always wants and cost
/// nothing to list. When <see cref="BluetoothObdConfiguration.DiscoverUnpaired"/> is on, inquiries then
/// run back to back until the token is cancelled, so a UI stays live as adapters are powered up.
/// Cancel before connecting: an inquiry in progress slows RFCOMM connections on the same radio badly.
///
/// <see cref="ObdDiscoveredDevice.NativeDevice"/> is the <see cref="IBluetoothDevice"/>, and
/// <see cref="ObdDiscoveredDevice.Id"/> is its address - persist that to reconnect later.
/// </remarks>
public class BluetoothObdDeviceScanner : IObdDeviceScanner
{
    readonly IBluetoothManager manager;
    readonly BluetoothObdConfiguration config;
    readonly ILogger logger;

    public BluetoothObdDeviceScanner(
        IBluetoothManager manager,
        BluetoothObdConfiguration? config = null,
        ILogger<BluetoothObdDeviceScanner>? logger = null
    )
    {
        this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
        this.config = config ?? new BluetoothObdConfiguration();
        this.logger = logger ?? NullLogger<BluetoothObdDeviceScanner>.Instance;
    }

    public async Task Scan(Action<ObdDiscoveredDevice> onDeviceFound, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(onDeviceFound);

        var access = await this.manager.RequestAccess(ct).ConfigureAwait(false);
        if (access != AccessState.Available)
            throw new ObdException($"Bluetooth is not available ({access})");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var device in await this.manager.GetPairedDevices(ct).ConfigureAwait(false))
            this.Report(device, null, seen, onDeviceFound);

        var discover = this.config.DiscoverUnpaired &&
                       this.manager.Capabilities.HasFlag(BluetoothCapabilities.Discovery);

        try
        {
            if (!discover)
            {
                // Nothing more to find, but the contract is "completes when cancelled" - a picker
                // treats an early return as the scan having finished.
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                return;
            }

            while (!ct.IsCancellationRequested)
            {
                await foreach (var result in this.manager.Discover(ct).ConfigureAwait(false))
                    this.Report(result.Device, result.Rssi, seen, onDeviceFound);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    void Report(IBluetoothDevice device, int? rssi, HashSet<string> seen, Action<ObdDiscoveredDevice> onDeviceFound)
    {
        this.logger.LogDebug(
            "Bluetooth device - Name: {Name}, Address: {Address}, Paired: {Paired}, Class: {Class}, RSSI: {Rssi}",
            device.Name ?? "(none)",
            device.Address,
            device.IsPaired,
            device.MajorDeviceClass,
            rssi
        );

        if (!this.config.Matches(device.Name) || !seen.Add(device.Address))
            return;

        // Empty rather than null: ObdDiscoveredDevice.Name is non-nullable, and only an empty filter
        // list lets an unnamed device through.
        onDeviceFound(new ObdDiscoveredDevice(device.Name ?? string.Empty, device.Address, device));
    }
}
