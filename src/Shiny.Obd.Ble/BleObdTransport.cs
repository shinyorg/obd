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
/// <remarks>
/// Also implements <see cref="IDisposable"/> so a DI container can tear it down on its synchronous
/// disposal path — see the note on <see cref="ObdConnection"/>. Everything this closes is synchronous
/// anyway, so the two are equivalent.
/// </remarks>
public class BleObdTransport : IObdTransport, IDisposable
{
    readonly IBleManager? bleManager;
    readonly BleObdConfiguration config;
    readonly SemaphoreSlim sendLock = new(1, 1);
    readonly StringBuilder responseBuffer = new();

    /// <summary>
    /// Guards <see cref="responseBuffer"/> and <see cref="responseTcs"/>, which the BLE notification
    /// callback and the sending thread both touch.
    /// </summary>
    readonly object exchangeLock = new();

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

            // Match on the candidate's resolved name, not Peripheral.Name - on iOS the latter is null
            // while scanning, so a name filter here would never match. See BleScanCandidate.From.
            this.peripheral = await this.bleManager
                .Scan()
                .Select(BleScanCandidate.From)
                .Where(x => x.Matches(this.config.DeviceNameFilter))
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

        // A disconnect is an answer. Fail anything mid-exchange now rather than leaving the caller to
        // sit out the full command timeout for a reply that can no longer arrive.
        this.EndExchange(new ObdException("The OBD connection was closed"));

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
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (this.exchangeLock)
            {
                this.responseBuffer.Clear();
                this.responseTcs = tcs;
            }

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

            try
            {
                return await tcs.Task
                    .WaitAsync(cts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Our own deadline elapsed, not the caller's token. Report it as what it is so a
                // polling caller doesn't mistake a quiet adapter for its own shutdown.
                throw new ObdTimeoutException(command.Trim(), this.config.CommandTimeout);
            }
        }
        finally
        {
            // However this ended, the exchange is over. Clearing it before releasing the lock is what
            // stops a late reply to a timed-out command from completing the *next* command's wait
            // with the previous command's data — one timeout would otherwise put every response
            // after it off by one, and the session would never recover on its own.
            this.EndExchange();
            this.sendLock.Release();
        }
    }

    public void Dispose()
    {
        _ = this.Disconnect();
        this.peripheral = null;
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        _ = this.Disconnect();
        this.peripheral = null;

        // sendLock is deliberately left undisposed: a Send may still be unwinding on it, and
        // disposing a semaphore out from under a waiter throws ObjectDisposedException in the
        // caller's face. Nothing here uses AvailableWaitHandle, so there is no handle to release.
        return default;
    }

    void OnNotificationReceived(BleCharacteristicResult result)
    {
        if (result.Data == null)
            return;

        var text = Encoding.ASCII.GetString(result.Data);
        lock (this.exchangeLock)
        {
            // Nothing is waiting — this is a late reply to a command that already timed out or a
            // connection that has gone. Dropping it keeps it out of the next command's buffer.
            if (this.responseTcs == null)
                return;

            this.responseBuffer.Append(text);

            var current = this.responseBuffer.ToString();
            if (!current.Contains('>'))
                return;

            var response = current.Replace(">", "").Trim();
            this.responseTcs.TrySetResult(response);
        }
    }

    /// <summary>Closes off the current exchange, optionally failing whoever is waiting on it.</summary>
    void EndExchange(Exception? error = null)
    {
        lock (this.exchangeLock)
        {
            if (error != null)
                this.responseTcs?.TrySetException(error);

            this.responseTcs = null;
            this.responseBuffer.Clear();
        }
    }
}
