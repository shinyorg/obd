using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shiny.BluetoothLE;
using Shiny.Obd.Ble;

namespace Shiny.Obd.Tests;

public class BleObdDeviceScannerTests
{
    [Fact]
    public async Task Scan_SurfacesAdaptersThatAdvertiseNoName()
    {
        // The regression this guards. Requiring a name looks harmless and is not: the name is
        // `Peripheral.Name ?? AdvertisementData.LocalName`, and on iOS `CBPeripheral.Name` stays null
        // until CoreBluetooth has connected to that peripheral once and cached it. So a name requirement
        // is really a *first-connection-of-the-process* filter there - an adapter advertising from the
        // OBD port was invisible on exactly the attempt that mattered, and only became findable once a
        // connection had already succeeded by some other route. Plenty of ELM327 clones never put a name
        // in the advertisement at all.
        var nameless = new StubPeripheral(null);
        var found = await ScanFor(new ScanResult(nameless, -55, new StubAdvertisementData(null, null)));

        var device = Assert.Single(found);
        Assert.Equal(nameless.Uuid, device.Id);
        Assert.Equal(string.Empty, device.Name);
    }

    [Fact]
    public async Task Scan_SurfacesNamedAdaptersToo()
    {
        var peripheral = new StubPeripheral("OBDLink MX+");
        var found = await ScanFor(new ScanResult(peripheral, -55, new StubAdvertisementData(null, null)));

        Assert.Equal("OBDLink MX+", Assert.Single(found).Name);
    }

    [Fact]
    public async Task Scan_UsesTheAdvertisedNameWhenThePeripheralHasNone()
    {
        var found = await ScanFor(
            new ScanResult(new StubPeripheral(null), -55, new StubAdvertisementData("VEEPEAK", null))
        );

        Assert.Equal("VEEPEAK", Assert.Single(found).Name);
    }

    [Fact]
    public async Task Scan_AnExplicitNameFilterStillExcludesUnnamedAdapters()
    {
        // Dropping the unconditional name requirement must not weaken a filter the caller actually asked
        // for: Matches cannot match a name that isn't there, and that is the right answer here. Asking
        // for a name is the caller's to do; requiring one of everybody was not.
        var found = await ScanFor(
            new BleObdConfiguration { DeviceNameFilter = "veepeak" },
            new ScanResult(new StubPeripheral(null), -55, new StubAdvertisementData(null, null)),
            new ScanResult(new StubPeripheral("VEEPEAK OBDCheck"), -55, new StubAdvertisementData(null, null))
        );

        Assert.Equal("VEEPEAK OBDCheck", Assert.Single(found).Name);
    }

    [Fact]
    public async Task Scan_DeDupesRepeatedAdvertisementsByPeripheralId()
    {
        // Advertisements repeat several times a second; a picker bound to this must not grow a row each time.
        var peripheral = new StubPeripheral(null);
        var found = await ScanFor(
            new ScanResult(peripheral, -55, new StubAdvertisementData(null, null)),
            new ScanResult(peripheral, -60, new StubAdvertisementData(null, null))
        );

        Assert.Equal(peripheral.Uuid, Assert.Single(found).Id);
    }

    static Task<List<ObdDiscoveredDevice>> ScanFor(params ScanResult[] advertisements)
        => ScanFor(new BleObdConfiguration(), advertisements);

    static async Task<List<ObdDiscoveredDevice>> ScanFor(
        BleObdConfiguration config,
        params ScanResult[] advertisements
    )
    {
        var scanner = new BleObdDeviceScanner(new StubBleManager(advertisements), config);
        var found = new List<ObdDiscoveredDevice>();

        // The stub sequence completes on its own, so the scan ends without needing the token cancelled.
        await scanner.Scan(found.Add, CancellationToken.None);

        return found;
    }


    /// <summary>
    /// A radio that replays a fixed set of advertisements and then completes. Only <c>Scan</c> is
    /// implemented - the scanner touches nothing else, and anything that wandered further should say so.
    /// </summary>
    class StubBleManager(ScanResult[] advertisements) : IBleManager
    {
        public IObservable<ScanResult> Scan(ScanConfig? scanConfig = null)
            => advertisements.ToObservable();

        public AccessState CurrentAccess => AccessState.Available;
        public IObservable<AccessState> RequestAccess() => Observable.Return(AccessState.Available);
        public IPeripheral? GetKnownPeripheral(string peripheralUuid) => throw new NotSupportedException();
        public bool IsScanning => false;
        public void StopScan() => throw new NotSupportedException();
        public IEnumerable<IPeripheral> GetConnectedPeripherals() => throw new NotSupportedException();
    }
}
