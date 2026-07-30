using System;
using System.Collections.Generic;
using System.Reactive;
using Shiny.BluetoothLE;
using Shiny.Obd.Ble;

namespace Shiny.Obd.Tests;

public class BleScanCandidateTests
{
    [Fact]
    public void From_PrefersPeripheralName()
    {
        var result = Advertisement(peripheralName: "VEEPEAK", localName: "OBDII");
        var candidate = BleScanCandidate.From(result);

        Assert.Equal("VEEPEAK", candidate.Name);
    }

    [Fact]
    public void From_FallsBackToLocalName_WhenPeripheralHasNoName()
    {
        // This is the iOS case: CBPeripheral.Name is null while scanning a peripheral that has never
        // been connected to, and the only name available is the one in the advertisement.
        var result = Advertisement(peripheralName: null, localName: "VEEPEAK");
        var candidate = BleScanCandidate.From(result);

        Assert.Equal("VEEPEAK", candidate.Name);
    }

    [Fact]
    public void From_NameIsNull_WhenNeitherSourceHasOne()
    {
        var result = Advertisement(peripheralName: null, localName: null);
        var candidate = BleScanCandidate.From(result);

        Assert.Null(candidate.Name);
    }

    [Fact]
    public void From_CarriesRssiAndAdvertisedServices()
    {
        var result = Advertisement(peripheralName: "VEEPEAK", localName: null, rssi: -62, serviceUuids: ["FFF0"]);
        var candidate = BleScanCandidate.From(result);

        Assert.Equal(-62, candidate.Rssi);
        Assert.NotNull(candidate.ServiceUuids);
        Assert.Equal(["FFF0"], candidate.ServiceUuids);
    }

    [Fact]
    public void Matches_NullFilter_MatchesEverything()
    {
        Assert.True(Candidate("VEEPEAK").Matches(null));
        Assert.True(Candidate(null).Matches(null));
    }

    [Fact]
    public void Matches_IsCaseInsensitivePartialMatch()
    {
        Assert.True(Candidate("VEEPEAK OBDCheck").Matches("veepeak"));
        Assert.True(Candidate("VEEPEAK OBDCheck").Matches("obdcheck"));
    }

    [Fact]
    public void Matches_RejectsOtherNames()
        => Assert.False(Candidate("OBDLink MX+").Matches("veepeak"));

    [Fact]
    public void Matches_UnnamedDevice_FailsAnyFilter()
        => Assert.False(Candidate(null).Matches("veepeak"));

    [Fact]
    public void Matches_UsesAdvertisedName_WhenPeripheralHasNoName()
    {
        var candidate = BleScanCandidate.From(Advertisement(peripheralName: null, localName: "VEEPEAK"));

        Assert.True(candidate.Matches("veepeak"));
    }

    static BleScanCandidate Candidate(string? name)
        => new(new StubPeripheral(null), name, -50, null);

    static ScanResult Advertisement(
        string? peripheralName,
        string? localName,
        int rssi = -50,
        string[]? serviceUuids = null
    ) => new(
        new StubPeripheral(peripheralName),
        rssi,
        new StubAdvertisementData(localName, serviceUuids)
    );


    class StubAdvertisementData(string? localName, string[]? serviceUuids) : IAdvertisementData
    {
        public string? LocalName => localName;
        public string[]? ServiceUuids => serviceUuids;
        public bool? IsConnectable => true;
        public AdvertisementServiceData[]? ServiceData => null;
        public ManufacturerData? ManufacturerData => null;
        public int? TxPower => null;
    }


    /// <summary>
    /// Only the members the scan pipeline touches are implemented - everything else is out of scope for
    /// these tests and throws rather than pretending to work.
    /// </summary>
    class StubPeripheral(string? name) : IPeripheral
    {
        public string Uuid { get; } = Guid.NewGuid().ToString();
        public string? Name => name;

        public int Mtu => throw new NotSupportedException();
        public ConnectionState Status => throw new NotSupportedException();
        public void Connect(ConnectionConfig? config) => throw new NotSupportedException();
        public void CancelConnection() => throw new NotSupportedException();
        public IObservable<ConnectionState> WhenStatusChanged() => throw new NotSupportedException();
        public IObservable<BleException> WhenConnectionFailed() => throw new NotSupportedException();
        public IObservable<Unit> WhenServicesChanged() => throw new NotSupportedException();
        public IObservable<int> ReadRssi() => throw new NotSupportedException();
        public IObservable<BleServiceInfo> GetService(string serviceUuid) => throw new NotSupportedException();
        public IObservable<IReadOnlyList<BleServiceInfo>> GetServices() => throw new NotSupportedException();
        public IObservable<BleCharacteristicInfo> GetCharacteristic(string serviceUuid, string characteristicUuid) => throw new NotSupportedException();
        public IObservable<IReadOnlyList<BleCharacteristicInfo>> GetCharacteristics(string serviceUuid) => throw new NotSupportedException();
        public IObservable<BleCharacteristicResult> NotifyCharacteristic(string serviceUuid, string characteristicUuid, bool useIndicationsIfAvailable = true) => throw new NotSupportedException();
        public IObservable<BleCharacteristicInfo> WhenCharacteristicSubscriptionChanged(string serviceUuid, string characteristicUuid) => throw new NotSupportedException();
        public IObservable<BleCharacteristicResult> ReadCharacteristic(string serviceUuid, string characteristicUuid) => throw new NotSupportedException();
        public IObservable<BleCharacteristicResult> WriteCharacteristic(string serviceUuid, string characteristicUuid, byte[] data, bool withResponse = true) => throw new NotSupportedException();
        public IObservable<BleDescriptorInfo> GetDescriptor(string serviceUuid, string characteristicUuid, string descriptorUuid) => throw new NotSupportedException();
        public IObservable<IReadOnlyList<BleDescriptorInfo>> GetDescriptors(string serviceUuid, string characteristicUuid) => throw new NotSupportedException();
        public IObservable<BleDescriptorResult> ReadDescriptor(string serviceUuid, string characteristicUuid, string descriptorUuid) => throw new NotSupportedException();
        public IObservable<BleDescriptorResult> WriteDescriptor(string serviceUuid, string characteristicUuid, string descriptorUuid, byte[] data) => throw new NotSupportedException();
    }
}
