using Shiny.BluetoothLE;

namespace Shiny.Obd.Ble;

/// <summary>
/// A single BLE advertisement reduced to the pieces the scan pipeline filters and logs on.
/// </summary>
/// <param name="Peripheral">The advertising peripheral.</param>
/// <param name="Name">
/// The best name available at scan time - see <see cref="From"/> for why this isn't simply
/// <c>Peripheral.Name</c>.
/// </param>
/// <param name="Rssi">Signal strength of this advertisement in dBm.</param>
/// <param name="ServiceUuids">Service UUIDs carried in the advertisement, if the adapter advertised any.</param>
internal record BleScanCandidate(
    IPeripheral Peripheral,
    string? Name,
    int Rssi,
    string[]? ServiceUuids
)
{
    /// <summary>
    /// Projects a scan result, falling back to the advertised local name when the peripheral has no
    /// name yet. iOS leaves <c>CBPeripheral.Name</c> null while scanning for a peripheral it has
    /// never connected to - the name is only in the advertisement payload - so filtering on
    /// <c>Peripheral.Name</c> alone silently discards nearly every adapter on that platform.
    /// </summary>
    public static BleScanCandidate From(ScanResult result) => new(
        result.Peripheral,
        result.Peripheral.Name ?? result.AdvertisementData?.LocalName,
        result.Rssi,
        result.AdvertisementData?.ServiceUuids
    );

    /// <summary>
    /// True when this advertisement matches the configured name filter. A null filter matches everything,
    /// including advertisements that carry no name at all.
    /// </summary>
    public bool Matches(string? deviceNameFilter)
        => deviceNameFilter == null ||
           (this.Name?.Contains(deviceNameFilter, StringComparison.OrdinalIgnoreCase) ?? false);
}
