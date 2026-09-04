using System;
using System.Collections.Generic;
using System.Reactive;
using Shiny.BluetoothLE;

namespace Shiny.Obd.Tests;

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
