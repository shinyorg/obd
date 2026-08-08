using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Shiny;
using Shiny.Obd;
using Shiny.Obd.Ble;
using Shiny.Obd.Commands;

namespace Sample.Maui;

/// <summary>
/// The home screen. The top of it is the gauge cluster - the six readings polled continuously - and
/// below that is every command <c>Shiny.Obd</c> can issue, run on demand from the Read button.
/// </summary>
/// <remarks>
/// The gauges and the sweep share one connection, so the poll loop stands down while a sweep is
/// running: two callers interleaving requests on one adapter turns a sweep into a crawl.
/// <para>
/// The sweep asks for things in the order a client should ask for them: walk the support masks, then
/// only ask for what the vehicle said it has. That gating is the point of
/// <see cref="SupportedPidsCommand"/> - probing blind works, but spends a round trip per PID to be
/// told NO DATA.
/// </para>
/// </remarks>
[ShellMap<DashboardPage>("dashboard")]
public partial class DashboardViewModel(
    BleObdConfiguration config
) : ObservableObject, IPageLifecycleAware, IDisposable
{
    /// <summary>Shown in place of a reading the vehicle did not give us.</summary>
    const string NoReading = "--";

    static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Display and execution order for the sweep. The masks have to run before anything gates on them.</summary>
    static readonly string[] GroupOrder =
    [
        ObdCommandCatalog.SupportGroup,
        ObdCommandCatalog.LiveGroup,
        ObdCommandCatalog.FreezeFrameGroup,
        ObdCommandCatalog.DtcGroup,
        ObdCommandCatalog.InfoGroup,

        // Last, because the monitors the mask discovers are appended to the end of the list mid-sweep.
        ObdCommandCatalog.TestGroup
    ];

    readonly List<ObdCommandRun> all = [.. ObdCommandCatalog
        .Build()
        .OrderBy(x => Array.IndexOf(GroupOrder, x.Group))
        .Select(x => new ObdCommandRun(x))];

    /// <summary>Names of the gauges that did not answer on the cycle currently being read.</summary>
    readonly List<string> gaugeFaults = [];

    IObdConnection? connection;
    CancellationTokenSource? pollCts;
    CancellationTokenSource? sweepCts;
    bool clearArmed;

    // ---- Connection ---------------------------------------------------------------------------

    [ObservableProperty] string deviceName = "Unknown";
    [ObservableProperty] string status = "Waiting...";
    [ObservableProperty] bool isConnected;
    [ObservableProperty] string connectionButtonText = "Connect";
    [ObservableProperty] string adapterInfo = "--";

    // ---- Gauges -------------------------------------------------------------------------------
    // Strings rather than numbers: a PID the vehicle will not answer has to read as absent, and a
    // numeric property can only fall back to zero - which shows as a real measurement.

    [ObservableProperty] string speed = NoReading;
    [ObservableProperty] string rpm = NoReading;
    [ObservableProperty] string coolantTemp = NoReading;
    [ObservableProperty] string throttle = NoReading;
    [ObservableProperty] string fuelLevel = NoReading;
    [ObservableProperty] string engineLoad = NoReading;

    // ---- Sweep --------------------------------------------------------------------------------

    public ObservableCollection<ObdCommandRun> Runs { get; } = new();

    [ObservableProperty] bool isReading;
    [ObservableProperty] string readButtonText = "Read";
    [ObservableProperty] string lastRead = "Last read: never";
    [ObservableProperty] string readStatus = "Nothing read yet — tap Read to run every command once.";
    [ObservableProperty] string search = "";
    [ObservableProperty] string clearButtonText = "Clear DTCs";

    /// <summary>
    /// Ask for every mode 01 PID whether the mask claims it or not. Off by default because that is the
    /// wrong way to talk to a vehicle - but it is the only way to catch an ECU whose mask under-reports
    /// what it will actually answer, which does happen.
    /// </summary>
    [ObservableProperty] bool probeUnsupported;

    public ObdDiscoveredDevice? Device { get; set; }

    public string Summary
    {
        get
        {
            var answered = this.all.Count(x => x.State == ObdCommandState.Answered);
            var failed = this.all.Count(x => x.State is ObdCommandState.Failed or ObdCommandState.NoData);
            var skipped = this.all.Count(x => x.State == ObdCommandState.Unsupported);
            return $"{this.all.Count} commands — {answered} answered, {skipped} skipped, {failed} failed";
        }
    }

    partial void OnIsConnectedChanged(bool value)
        => this.ConnectionButtonText = value ? "Disconnect" : "Connect";

    partial void OnIsReadingChanged(bool value)
        => this.ReadButtonText = value ? "Cancel" : "Read";

    partial void OnSearchChanged(string value) => this.ApplyFilter();

    public void OnAppearing()
    {
        this.DeviceName = this.Device?.Name ?? "Unknown";
        this.ApplyFilter();
        _ = this.ConnectToDevice();
    }

    public void OnDisappearing()
    {
        this.sweepCts?.Cancel();
        _ = this.DisconnectFromDevice();
    }

    [RelayCommand]
    async Task ToggleConnection()
    {
        if (this.IsConnected)
            await this.DisconnectFromDevice();
        else
            await this.ConnectToDevice();
    }

    [RelayCommand]
    Task Read() => this.IsReading ? this.CancelSweep() : this.RunSweepGuarded();

    /// <summary>
    /// The one command here that writes rather than reads, so it takes a second tap. Clearing wipes
    /// stored and pending codes, the freeze frame and every monitor's readiness - which is a slow thing
    /// to undo, since readiness only comes back over several drive cycles.
    /// </summary>
    [RelayCommand]
    async Task ClearDtcs()
    {
        if (!this.clearArmed)
        {
            this.clearArmed = true;
            this.ClearButtonText = "Tap again to erase fault memory";
            return;
        }

        this.clearArmed = false;
        this.ClearButtonText = "Clear DTCs";

        if (this.connection is not { IsConnected: true })
        {
            this.ReadStatus = "Not connected";
            return;
        }

        var run = new ObdCommandRun(ObdCommandCatalog.ClearDtcs());
        try
        {
            await this.Execute(run, CancellationToken.None);
            this.ReadStatus = $"Clear DTCs — {run.Value}";
        }
        catch (Exception ex)
        {
            this.ReadStatus = $"Clear failed: {ex.Message}";
        }
    }

    // ---- Connection ---------------------------------------------------------------------------

    async Task ConnectToDevice()
    {
        if (this.Device == null) return;

        try
        {
            this.Status = "Connecting...";
            var transport = new BleObdTransport(this.Device, config);
            this.connection = new ObdConnection(transport);
            await this.connection.Connect();

            this.IsConnected = true;
            var adapter = (this.connection as ObdConnection)?.DetectedAdapter;
            this.AdapterInfo = adapter?.RawIdentifier ?? "Unknown adapter";
            this.Status = "Connected";

            this.pollCts?.Cancel();
            this.pollCts?.Dispose();
            this.pollCts = new CancellationTokenSource();
            _ = this.PollLoop(this.pollCts.Token);
        }
        catch (Exception ex)
        {
            this.Status = $"Error: {ex.Message}";
            this.IsConnected = false;
        }
    }

    async Task DisconnectFromDevice()
    {
        this.pollCts?.Cancel();
        this.pollCts?.Dispose();
        this.pollCts = null;

        if (this.connection != null)
        {
            await this.connection.Disconnect();
            await this.connection.DisposeAsync();
            this.connection = null;
        }
        this.IsConnected = false;
        this.Status = "Disconnected";
        this.ClearGauges();
    }

    // ---- Gauges -------------------------------------------------------------------------------

    async Task PollLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && this.connection?.IsConnected == true)
        {
            // A sweep owns the adapter while it runs. Polling through it would double the traffic on a
            // link that is already the slow part.
            if (!this.IsReading)
            {
                try
                {
                    await this.ReadGauges(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            try { await Task.Delay(PollInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    async Task ReadGauges(CancellationToken ct)
    {
        this.gaugeFaults.Clear();

        this.Speed = Show(await this.Gauge("Speed", StandardCommands.VehicleSpeed, ct), "0");
        this.Rpm = Show(await this.Gauge("RPM", StandardCommands.EngineRpm, ct), "0");
        this.CoolantTemp = Show(await this.Gauge("Coolant", StandardCommands.CoolantTemperature, ct), "0");
        this.Throttle = Show(await this.Gauge("Throttle", StandardCommands.ThrottlePosition, ct), "0.0");
        this.FuelLevel = Show(await this.Gauge("Fuel level", StandardCommands.FuelLevel, ct), "0.0");
        this.EngineLoad = Show(await this.Gauge("Engine load", StandardCommands.CalculatedEngineLoad, ct), "0.0");

        this.Status = this.gaugeFaults.Count == 0
            ? $"Connected — last update {DateTime.Now:HH:mm:ss}"
            : $"Connected — last update {DateTime.Now:HH:mm:ss} · {String.Join(", ", this.gaugeFaults)}";
    }

    /// <summary>
    /// Reads one gauge, and answers null rather than throwing when the vehicle will not give it up.
    /// </summary>
    /// <remarks>
    /// Each tile gets its own try/catch on purpose. One try around the whole cycle meant the first PID
    /// the vehicle declined - fuel level and coolant are the usual ones, and any of them can time out on
    /// a busy bus - aborted the cycle, so every reading after it in the list stayed at its default
    /// forever. That looks exactly like four broken gauges rather than one unanswered PID.
    /// </remarks>
    async Task<T?> Gauge<T>(string name, IObdCommand<T> command, CancellationToken ct) where T : struct
    {
        try
        {
            return await this.connection!.Execute(command, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            this.gaugeFaults.Add($"{name}: {ex.Message}");
            return null;
        }
    }

    static string Show<T>(T? value, string format) where T : struct, IFormattable
        => value?.ToString(format, CultureInfo.CurrentCulture) ?? NoReading;

    void ClearGauges()
    {
        this.Speed = NoReading;
        this.Rpm = NoReading;
        this.CoolantTemp = NoReading;
        this.Throttle = NoReading;
        this.FuelLevel = NoReading;
        this.EngineLoad = NoReading;
    }

    // ---- Sweep --------------------------------------------------------------------------------

    async Task RunSweepGuarded()
    {
        if (this.Device == null)
        {
            this.ReadStatus = "No adapter selected — pick one on the Scan tab";
            return;
        }

        // Set before the first await: connecting takes long enough to tap the button again, and a
        // second connection to the same adapter is not something the first one survives.
        if (this.IsReading)
            return;

        this.IsReading = true;

        try
        {
            if (this.connection is not { IsConnected: true })
            {
                await this.ConnectToDevice();

                if (this.connection is not { IsConnected: true })
                    return;
            }

            this.sweepCts = new CancellationTokenSource();
            await this.RunSweep(this.sweepCts.Token);

            this.LastRead = $"Last read: {DateTime.Now:HH:mm:ss} — {this.Summary}";
        }
        catch (OperationCanceledException)
        {
            this.ReadStatus = "Read cancelled";
            this.LastRead = $"Last read: {DateTime.Now:HH:mm:ss} — cancelled";
        }
        catch (Exception ex)
        {
            this.ReadStatus = $"Error: {ex.Message}";
        }
        finally
        {
            this.IsReading = false;
            this.sweepCts?.Dispose();
            this.sweepCts = null;
            this.OnPropertyChanged(nameof(this.Summary));
        }
    }

    Task CancelSweep()
    {
        this.sweepCts?.Cancel();
        return Task.CompletedTask;
    }

    async Task RunSweep(CancellationToken ct)
    {
        // The monitors from the previous sweep were discovered, not declared - a different vehicle
        // reports a different set, so they go and get rediscovered.
        this.all.RemoveAll(x => x.Entry.Group == ObdCommandCatalog.TestGroup && x.Entry.DiscoveryBlock == null);
        foreach (var run in this.all)
            run.Reset();

        this.ApplyFilter();

        // ---- Discovery: what does this vehicle claim to have? --------------------------------------
        this.ReadStatus = "Walking the mode 01 support mask...";
        var supportedPids = await this.WalkMask(ObdCommandCatalog.SupportGroup, SupportedPidsCommand.BlockPids, ct);

        this.ReadStatus = "Walking the mode 06 monitor mask...";
        var supportedMids = await this.WalkMask(ObdCommandCatalog.TestGroup, MonitorIds.BlockMids, ct);

        foreach (var mid in supportedMids)
            this.all.Add(new ObdCommandRun(ObdCommandCatalog.OnBoardTest(mid)));

        this.ApplyFilter();

        // ---- Everything else, gated on what discovery found ---------------------------------------
        var freezeFrameStored = false;

        for (var i = 0; i < this.all.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var run = this.all[i];
            if (run.Entry.DiscoveryBlock != null || run.State != ObdCommandState.Pending)
                continue;

            var group = run.Entry.Group;

            // The causal DTC is the first row of its group and gates the rest of it: with no snapshot
            // stored the frame reads back zero-filled, which looks like 0% load at -40 °C rather than
            // like an absent answer.
            if (group == ObdCommandCatalog.FreezeFrameGroup && run.Entry.SupportPid == null)
            {
                await this.Execute(run, ct);
                freezeFrameStored = run.State == ObdCommandState.Answered && run.Value != "(none)";

                if (!freezeFrameStored)
                    this.ReadStatus = "No freeze frame stored — skipping mode 02";

                continue;
            }

            if (group == ObdCommandCatalog.FreezeFrameGroup && !freezeFrameStored)
            {
                run.State = ObdCommandState.Unsupported;
                run.Value = "no snapshot stored";
                continue;
            }

            if (!this.ProbeUnsupported && run.Entry.SupportPid is { } pid && !supportedPids.Contains(pid))
            {
                run.State = ObdCommandState.Unsupported;
                continue;
            }

            this.ReadStatus = $"{run.Request} — {run.Name}";
            await this.Execute(run, ct);
        }

        this.ReadStatus = this.Summary;
    }

    /// <summary>
    /// Walks a block of support-mask commands, stopping at the first block the vehicle does not
    /// advertise. Blocks past that point are marked skipped rather than asked - a vehicle that does not
    /// claim block 0x40 is not going to answer it.
    /// </summary>
    async Task<HashSet<byte>> WalkMask(string group, byte[] blocks, CancellationToken ct)
    {
        var supported = new HashSet<byte>();
        var rows = this.all
            .Where(x => x.Entry.Group == group && x.Entry.DiscoveryBlock != null)
            .ToDictionary(x => x.Entry.DiscoveryBlock!.Value);

        foreach (var block in blocks)
        {
            ct.ThrowIfCancellationRequested();

            if (!rows.TryGetValue(block, out var run))
                continue;

            // The first block is always worth asking. Every later one is only worth asking when the
            // previous mask named it.
            if (block != blocks[0] && !supported.Contains(block))
            {
                run.State = ObdCommandState.Unsupported;
                continue;
            }

            var result = await this.Execute(run, ct);
            if (result is IReadOnlyList<byte> pids)
                supported.UnionWith(pids);
        }

        // The block PIDs are the masks themselves, not readings, so they never gate a command.
        supported.ExceptWith(blocks);
        return supported;
    }

    async Task<object?> Execute(ObdCommandRun run, CancellationToken ct)
    {
        run.State = ObdCommandState.Running;
        var started = Stopwatch.GetTimestamp();

        try
        {
            var result = await run.Entry.Execute(this.connection!, ct);
            run.Value = ObdValueFormatter.Format(result);
            run.ElapsedMs = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            run.State = ObdCommandState.Answered;
            return result;
        }
        catch (OperationCanceledException)
        {
            run.Reset();
            throw;
        }
        catch (ObdException ex)
        {
            // NO DATA is the vehicle declining a PID it does not have, which is an answer rather than a
            // fault - worth telling apart from a reply that arrived and would not parse.
            run.ElapsedMs = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            run.Value = ex.Message;
            run.State = ex.Message.Contains("No data") ? ObdCommandState.NoData : ObdCommandState.Failed;
            return null;
        }
        catch (Exception ex)
        {
            run.ElapsedMs = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            run.Value = ex.Message;
            run.State = ObdCommandState.Failed;
            return null;
        }
        finally
        {
            this.OnPropertyChanged(nameof(this.Summary));
        }
    }

    void ApplyFilter()
    {
        var term = this.Search?.Trim() ?? "";

        var matches = this.all.Where(x =>
            term.Length == 0 ||
            x.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            x.Request.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            x.Group.Contains(term, StringComparison.OrdinalIgnoreCase)
        );

        this.Runs.Clear();
        foreach (var run in matches)
            this.Runs.Add(run);
    }

    public void Dispose()
    {
        this.sweepCts?.Cancel();
        this.sweepCts?.Dispose();
        this.pollCts?.Cancel();
        this.pollCts?.Dispose();
    }
}
