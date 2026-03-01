using System;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Shiny.BluetoothLE;

namespace Shiny.Obd.Ble;

/// <summary>
/// BLE transport for OBD communication using Shiny.BluetoothLE.
/// Works with ELM327-compatible BLE adapters.
/// </summary>
public class BleObdTransport : IObdTransport
{
    readonly IBleManager? bleManager;
    readonly BleObdConfiguration config;
    readonly SemaphoreSlim sendLock = new(1, 1);
    readonly StringBuilder responseBuffer = new();

    IPeripheral? peripheral;
    IDisposable? notificationSub;
    TaskCompletionSource<string>? responseTcs;

    /// <summary>
    /// Create a transport that will scan for a BLE OBD adapter
    /// </summary>
    public BleObdTransport(IBleManager bleManager, BleObdConfiguration config)
    {
        this.bleManager = bleManager ?? throw new ArgumentNullException(nameof(bleManager));
        this.config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Create a transport using a pre-discovered peripheral
    /// </summary>
    public BleObdTransport(IPeripheral peripheral, BleObdConfiguration config)
    {
        this.peripheral = peripheral ?? throw new ArgumentNullException(nameof(peripheral));
        this.config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Create a transport from a discovered device (obtained via <see cref="BleObdDeviceScanner"/>)
    /// </summary>
    public BleObdTransport(ObdDiscoveredDevice device, BleObdConfiguration config)
    {
        if (device == null) throw new ArgumentNullException(nameof(device));
        this.peripheral = device.NativeDevice as IPeripheral
            ?? throw new ArgumentException("Device is not a BLE peripheral", nameof(device));
        this.config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public bool IsConnected => this.peripheral?.Status == ConnectionState.Connected;

    public async Task Connect(CancellationToken ct = default)
    {
        if (this.peripheral == null)
        {
            if (this.bleManager == null)
                throw new ObdException("No peripheral or BLE manager provided");

            this.peripheral = await this.bleManager
                .Scan()
                .Where(x =>
                    this.config.DeviceNameFilter == null ||
                    (x.Peripheral.Name?.Contains(this.config.DeviceNameFilter, StringComparison.OrdinalIgnoreCase) ?? false))
                .Select(x => x.Peripheral)
                .Take(1)
                .ToTask(ct)
                .ConfigureAwait(false);
        }

        await this.peripheral.ConnectAsync(cancelToken: ct).ConfigureAwait(false);

        this.notificationSub = this.peripheral
            .NotifyCharacteristic(
                this.config.ServiceUuid,
                this.config.ReadCharacteristicUuid)
            .Subscribe(this.OnNotificationReceived);
    }

    public Task Disconnect()
    {
        this.notificationSub?.Dispose();
        this.notificationSub = null;
        this.peripheral?.CancelConnection();
        return Task.CompletedTask;
    }

    public async Task<string> Send(string command, CancellationToken ct = default)
    {
        if (this.peripheral == null || !this.IsConnected)
            throw new ObdException("Not connected to OBD adapter");

        await this.sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            this.responseBuffer.Clear();
            this.responseTcs = new TaskCompletionSource<string>();

            var bytes = Encoding.ASCII.GetBytes(command);
            await this.peripheral.WriteCharacteristicAsync(
                this.config.ServiceUuid,
                this.config.WriteCharacteristicUuid,
                bytes,
                false,
                ct
            ).ConfigureAwait(false);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(this.config.CommandTimeout);

            return await this.responseTcs.Task
                .WaitAsync(cts.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            this.sendLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        this.notificationSub?.Dispose();
        this.peripheral?.CancelConnection();
        this.sendLock.Dispose();
        return default;
    }

    void OnNotificationReceived(BleCharacteristicResult result)
    {
        if (result.Data == null)
            return;

        var text = Encoding.ASCII.GetString(result.Data);
        this.responseBuffer.Append(text);

        var current = this.responseBuffer.ToString();
        if (current.Contains('>'))
        {
            var response = current.Replace(">", "").Trim();
            this.responseTcs?.TrySetResult(response);
        }
    }
}
