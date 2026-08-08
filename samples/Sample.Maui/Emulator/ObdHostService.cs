using System.Collections.ObjectModel;

namespace Sample.Maui.Emulator;

/// <summary>
/// Starts and stops both front-ends and collects what they see, so the UI has one thing to bind to.
/// </summary>
/// <remarks>
/// Started from the Adapter tab rather than at launch: a device that is only here to read a real
/// adapter has no business advertising a GATT service and holding a TCP listener open. Both transports
/// then run at the same time and share one vehicle: a value you change is answered identically over
/// BLE and TCP.
/// </remarks>
public partial class ObdHostService(
    BleObdPeripheral ble,
    TcpObdServer tcp,
    ObdHostConfiguration config
) : ObservableObject, IObdHostSink
{
    /// <summary>Enough history to see an initialisation handshake and a few polling cycles.</summary>
    const int MaxLogEntries = 250;

    readonly SemaphoreSlim gate = new(1, 1);

    public ObservableCollection<HostedClient> Clients { get; } = [];

    public ObservableCollection<ObdLogEntry> Log { get; } = [];

    public ObdHostConfiguration Configuration => config;

    [ObservableProperty] bool isRunning;
    [ObservableProperty] string bleStatus = "Not started";
    [ObservableProperty] string tcpStatus = "Not started";
    [ObservableProperty] string mdnsStatus = "Not published";
    [ObservableProperty] string tcpEndpoints = "-";

    public bool HasClients => this.Clients.Count > 0;

    public bool HasNoClients => this.Clients.Count == 0;

    public string ClientSummary => this.Clients.Count switch
    {
        0 => "No client connected",
        1 => "1 client connected",
        var n => $"{n} clients connected"
    };

    public async Task Start()
    {
        await this.gate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (this.IsRunning)
                return;

            // Deliberately independent: a device with no BLE peripheral support (or Bluetooth switched
            // off) should still come up as a WiFi adapter, and vice versa.
            await ble.Start(this).ConfigureAwait(true);
            this.BleStatus = ble.Status;

            await tcp.Start(this).ConfigureAwait(true);
            this.TcpStatus = tcp.Status;
            this.MdnsStatus = tcp.MdnsStatus;
            this.TcpEndpoints = tcp.Endpoints.Count == 0 ? "no network" : String.Join("\n", tcp.Endpoints);

            this.IsRunning = ble.IsRunning || tcp.IsRunning;
        }
        catch (Exception ex)
        {
            this.Failed("host", ex);
        }
        finally
        {
            this.gate.Release();
        }
    }

    public async Task Stop()
    {
        await this.gate.WaitAsync().ConfigureAwait(true);
        try
        {
            ble.Stop();
            await tcp.Stop().ConfigureAwait(true);

            this.BleStatus = ble.Status;
            this.TcpStatus = tcp.Status;
            this.MdnsStatus = tcp.MdnsStatus;
            this.TcpEndpoints = "-";
            this.IsRunning = false;

            this.OnMainThread(() =>
            {
                this.Clients.Clear();
                this.RaiseClientCounts();
            });
        }
        finally
        {
            this.gate.Release();
        }
    }

    public async Task Restart()
    {
        await this.Stop().ConfigureAwait(true);
        await this.Start().ConfigureAwait(true);
    }

    // ---- IObdHostSink -----------------------------------------------------------------------------

    public void ClientConnected(HostedClient client) => this.OnMainThread(() =>
    {
        if (this.Clients.Any(x => x.Id == client.Id))
            return;

        this.Clients.Add(client);
        this.RaiseClientCounts();
        this.Append(new ObdLogEntry(DateTime.Now, client.Transport, "(connected)", client.Address, "client connected"));
    });

    public void ClientDisconnected(string id) => this.OnMainThread(() =>
    {
        var existing = this.Clients.FirstOrDefault(x => x.Id == id);
        if (existing == null)
            return;

        this.Clients.Remove(existing);
        this.RaiseClientCounts();
        this.Append(new ObdLogEntry(DateTime.Now, existing.Transport, "(disconnected)", existing.Address, "client disconnected"));
    });

    public void Exchanged(HostedClient client, ObdExchange exchange) => this.OnMainThread(() =>
    {
        // The transports create their own HostedClient instance; bind updates to the one already in
        // the collection so the row the UI is showing is the row that moves.
        var tracked = this.Clients.FirstOrDefault(x => x.Id == client.Id);
        if (tracked == null)
        {
            tracked = client;
            this.Clients.Add(tracked);
            this.RaiseClientCounts();
        }

        tracked.RequestCount++;
        tracked.LastRequest = exchange.Request;

        this.Append(new ObdLogEntry(
            DateTime.Now,
            client.Transport,
            exchange.Request,
            exchange.Summary,
            exchange.Description
        ));
    });

    public void Failed(string transport, Exception ex) => this.OnMainThread(
        () => this.Append(new ObdLogEntry(DateTime.Now, transport, "(error)", ex.Message, ex.GetType().Name))
    );

    public void ClearLog() => this.OnMainThread(this.Log.Clear);

    // ---- Plumbing ---------------------------------------------------------------------------------

    void Append(ObdLogEntry entry)
    {
        // Newest first - the interesting line is the one that just arrived, and a log you have to
        // scroll to the bottom of is useless while you are watching a handshake go past.
        this.Log.Insert(0, entry);

        while (this.Log.Count > MaxLogEntries)
            this.Log.RemoveAt(this.Log.Count - 1);
    }

    void RaiseClientCounts()
    {
        this.OnPropertyChanged(nameof(this.HasClients));
        this.OnPropertyChanged(nameof(this.HasNoClients));
        this.OnPropertyChanged(nameof(this.ClientSummary));
    }

    void OnMainThread(Action action)
    {
        if (MainThread.IsMainThread)
            action();
        else
            MainThread.BeginInvokeOnMainThread(action);
    }
}
