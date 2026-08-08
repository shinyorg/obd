using System.Text;
using Shiny;
using Shiny.BluetoothLE.Hosting;

namespace Sample.Maui.Emulator;

/// <summary>
/// Presents the emulator as a BLE OBD adapter: a GATT service with a write characteristic for
/// commands and a notify characteristic for replies, advertised under the adapter's local name.
/// </summary>
/// <remarks>
/// <c>IPeripheral</c> here is the hosting one - a connected central - not the client library's
/// <c>Shiny.BluetoothLE.IPeripheral</c>. Both namespaces exist in this project, so this file
/// deliberately imports only the hosting one.
/// </remarks>
public sealed class BleObdPeripheral(
    IBleHostingManager manager,
    Elm327Responder responder,
    ObdHostConfiguration config
)
{
    /// <summary>
    /// The default ATT payload on a 23-byte MTU, and what the real adapters emit regardless of what
    /// the MTU negotiated up to. Responses are split across notifications the same way, so a client
    /// that reassembles by looking for the '>' prompt gets exercised properly.
    /// </summary>
    const int NotifyChunkSize = 20;

    readonly Dictionary<string, BleClient> clients = [];
    readonly object gate = new();

    IGattCharacteristic? notifyCharacteristic;

    public bool IsRunning { get; private set; }

    public string Status { get; private set; } = "Not started";

    public async Task<bool> Start(IObdHostSink sink)
    {
        if (this.IsRunning)
            return true;

        var access = await manager.RequestAccess().ConfigureAwait(true);
        if (access != AccessState.Available)
        {
            this.Status = access switch
            {
                AccessState.NotSupported => "This device cannot act as a BLE peripheral",
                AccessState.Disabled => "Bluetooth is off",
                AccessState.Denied => "Bluetooth permission denied",
                AccessState.NotSetup => "Bluetooth permission not configured",
                _ => $"BLE hosting unavailable ({access})"
            };
            return false;
        }

        // Re-registering a service that is already there throws; starting from empty makes a restart
        // after an error behave the same as a first start.
        manager.ClearServices();

        await manager.AddService(config.ServiceUuid, true, sb =>
        {
            this.notifyCharacteristic = sb.AddCharacteristic(config.NotifyCharacteristicUuid, cb =>
            {
                cb.SetNotification(
                    sub =>
                    {
                        // A central subscribing to the response characteristic is the closest thing GATT
                        // gives us to "a client has arrived" - it is the first thing any OBD app does.
                        if (sub.IsSubscribing)
                            sink.ClientConnected(this.Track(sub.Peripheral).Client);
                        else
                            this.Forget(sub.Peripheral, sink);

                        return Task.CompletedTask;
                    },
                    NotificationOptions.Notify
                );
            });

            sb.AddCharacteristic(config.WriteCharacteristicUuid, cb =>
            {
                cb.SetWrite(
                    request => this.OnWrite(request, sink),

                    // Shiny's BLE transport writes without a response, which is what real adapters
                    // expect. WriteOptions.Write is zero in this flags enum, so this value covers both.
                    WriteOptions.WriteWithoutResponse
                );
            });
        }).ConfigureAwait(true);

        await manager.StartAdvertising(new AdvertisementOptions(
            LocalName: config.LocalName,
            ServiceUuids: config.ServiceUuid
        )).ConfigureAwait(true);

        this.IsRunning = true;
        this.Status = $"Advertising as \"{config.LocalName}\" on {config.ServiceUuid}";
        return true;
    }

    public void Stop()
    {
        if (manager.IsAdvertising)
            manager.StopAdvertising();

        manager.ClearServices();
        this.notifyCharacteristic = null;

        lock (this.gate)
            this.clients.Clear();

        this.IsRunning = false;
        this.Status = "Stopped";
    }

    async Task OnWrite(WriteRequest request, IObdHostSink sink)
    {
        try
        {
            var client = this.Track(request.Peripheral);

            // A command may arrive split across writes, and several may arrive in one. Buffer until
            // the carriage return that terminates an ELM327 command, then answer each one in turn.
            List<string> commands = [];
            lock (this.gate)
            {
                client.Buffer.Append(Encoding.ASCII.GetString(request.Data));

                var pending = client.Buffer.ToString();
                var end = pending.LastIndexOf('\r');
                if (end >= 0)
                {
                    commands.AddRange(pending[..end].Split('\r', StringSplitOptions.RemoveEmptyEntries));
                    client.Buffer.Clear();
                    client.Buffer.Append(pending[(end + 1)..]);
                }
            }

            if (request.IsReplyNeeded)
                request.Respond(GattState.Success);

            foreach (var command in commands)
            {
                var exchange = responder.Respond(client.Session, command);
                sink.Exchanged(client.Client, exchange);

                await this.Send(client.Session.Render(exchange), request.Peripheral).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            sink.Failed("BLE", ex);

            if (request.IsReplyNeeded)
                request.Respond(GattState.Failure);
        }
    }

    async Task Send(string response, IPeripheral central)
    {
        var characteristic = this.notifyCharacteristic;
        if (characteristic == null)
            return;

        var bytes = Encoding.ASCII.GetBytes(response);
        for (var offset = 0; offset < bytes.Length; offset += NotifyChunkSize)
        {
            var length = Math.Min(NotifyChunkSize, bytes.Length - offset);
            var chunk = bytes.AsSpan(offset, length).ToArray();

            await characteristic.Notify(chunk, central).ConfigureAwait(false);
        }
    }

    BleClient Track(IPeripheral central)
    {
        lock (this.gate)
        {
            if (this.clients.TryGetValue(central.Uuid, out var existing))
                return existing;

            var client = new BleClient
            {
                Client = new HostedClient
                {
                    Id = central.Uuid,
                    Transport = "BLE",
                    Address = central.Uuid
                },
                Session = new Elm327Session(central.Uuid)
            };

            this.clients[central.Uuid] = client;
            return client;
        }
    }

    void Forget(IPeripheral central, IObdHostSink sink)
    {
        lock (this.gate)
            this.clients.Remove(central.Uuid);

        sink.ClientDisconnected(central.Uuid);
    }

    sealed class BleClient
    {
        public required HostedClient Client { get; init; }
        public required Elm327Session Session { get; init; }
        public StringBuilder Buffer { get; } = new();
    }
}
