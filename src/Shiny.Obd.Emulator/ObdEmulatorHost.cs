using System.Collections.ObjectModel;

namespace Shiny.Obd.Emulator;

/// <summary>
/// Starts and stops every registered transport and collects what they see, so there is one thing to
/// bind a UI to - or one thing to await in a test.
/// </summary>
/// <remarks>
/// <para>
/// Nothing starts at registration: a device that is only here to read a real adapter has no business
/// advertising a GATT service and holding a TCP listener open. Call <see cref="Start"/> when you want
/// to be an adapter.
/// </para>
/// <para>
/// All transports run at once and share one vehicle, so a value you change is answered identically
/// over every one of them.
/// </para>
/// </remarks>
public partial class ObdEmulatorHost(
    IEnumerable<IObdEmulatorTransport> transports,
    ObdEmulatorConfiguration config,
    IObdEmulatorDispatcher dispatcher
) : ObservableObject, IObdEmulatorSink
{
    /// <summary>Enough history to see an initialisation handshake and a few polling cycles.</summary>
    const int MaxLogEntries = 250;

    readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>Every transport the emulator can answer on, running or not.</summary>
    public IReadOnlyList<IObdEmulatorTransport> Transports { get; } = [.. transports];

    public ObservableCollection<HostedClient> Clients { get; } = [];

    public ObservableCollection<ObdLogEntry> Log { get; } = [];

    public ObdEmulatorConfiguration Configuration => config;

    [ObservableProperty] bool isRunning;

    public bool HasClients => this.Clients.Count > 0;

    public bool HasNoClients => this.Clients.Count == 0;

    public string ClientSummary => this.Clients.Count switch
    {
        0 => "No client connected",
        1 => "1 client connected",
        var n => $"{n} clients connected"
    };

    /// <summary>
    /// Brings up every transport. One that cannot start says so in its own
    /// <see cref="IObdEmulatorTransport.Status"/> and is skipped - the emulator is running as long as
    /// at least one client can reach it.
    /// </summary>
    public async Task Start(CancellationToken cancellationToken = default)
    {
        await this.gate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (this.IsRunning)
                return;

            foreach (var transport in this.Transports)
            {
                try
                {
                    await transport.Start(this, cancellationToken).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    // Deliberately independent: a device with no BLE peripheral support should still
                    // come up as a WiFi adapter, and vice versa.
                    this.Failed(transport.Name, ex);
                }
            }

            this.IsRunning = this.Transports.Any(x => x.IsRunning);
            this.RaiseTransportStatus();
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
            foreach (var transport in this.Transports)
            {
                try
                {
                    await transport.Stop().ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    this.Failed(transport.Name, ex);
                }
            }

            this.IsRunning = false;
            this.RaiseTransportStatus();

            dispatcher.Invoke(() =>
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

    public async Task Restart(CancellationToken cancellationToken = default)
    {
        await this.Stop().ConfigureAwait(true);
        await this.Start(cancellationToken).ConfigureAwait(true);
    }

    // ---- IObdEmulatorSink -------------------------------------------------------------------------

    public void ClientConnected(HostedClient client) => dispatcher.Invoke(() =>
    {
        if (this.Clients.Any(x => x.Id == client.Id))
            return;

        this.Clients.Add(client);
        this.RaiseClientCounts();
        this.Append(new ObdLogEntry(DateTime.Now, client.Transport, "(connected)", client.Address, "client connected"));
    });

    public void ClientDisconnected(string id) => dispatcher.Invoke(() =>
    {
        var existing = this.Clients.FirstOrDefault(x => x.Id == id);
        if (existing == null)
            return;

        this.Clients.Remove(existing);
        this.RaiseClientCounts();
        this.Append(new ObdLogEntry(DateTime.Now, existing.Transport, "(disconnected)", existing.Address, "client disconnected"));
    });

    public void Exchanged(HostedClient client, ObdExchange exchange) => dispatcher.Invoke(() =>
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

    public void Failed(string transport, Exception ex) => dispatcher.Invoke(
        () => this.Append(new ObdLogEntry(DateTime.Now, transport, "(error)", ex.Message, ex.GetType().Name))
    );

    public void ClearLog() => dispatcher.Invoke(this.Log.Clear);

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

    /// <summary>
    /// The transports are plain objects rather than observables - starting them all is the only thing
    /// that changes their status, so one nudge afterwards is enough to repaint.
    /// </summary>
    void RaiseTransportStatus() => dispatcher.Invoke(() => this.OnPropertyChanged(nameof(this.Transports)));
}
