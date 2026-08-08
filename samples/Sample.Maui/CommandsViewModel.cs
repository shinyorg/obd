using System.Collections.ObjectModel;
using System.Diagnostics;
using Shiny;
using Shiny.Obd;
using Shiny.Obd.Ble;
using Shiny.Obd.Commands;

namespace Sample.Maui;

/// <summary>
/// The Commands page: issues every command <c>Shiny.Obd</c> has against a live adapter and shows what
/// each one parsed back to.
/// </summary>
/// <remarks>
/// The Dashboard shows the six readings a gauge cluster wants. This shows the whole surface, in the
/// order a client should ask for it: walk the support masks, then only ask for what the vehicle said
/// it has. That gating is the point of <see cref="SupportedPidsCommand"/> - probing blind works, but
/// spends a round trip per PID to be told NO DATA.
/// </remarks>
[ShellMap<CommandsPage>("commands")]
public partial class CommandsViewModel(
    BleObdConfiguration config
) : ObservableObject, IPageLifecycleAware, IDisposable
{
    /// <summary>Display and execution order. The masks have to run before anything gates on them.</summary>
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

    IObdConnection? connection;
    CancellationTokenSource? sweepCts;
    bool clearArmed;

    public ObservableCollection<ObdCommandRun> Runs { get; } = new();

    [ObservableProperty] string status = "Waiting...";
    [ObservableProperty] string adapterInfo = "--";
    [ObservableProperty] bool isConnected;
    [ObservableProperty] bool isSweeping;
    [ObservableProperty] string search = "";
    [ObservableProperty] string clearButtonText = "Clear DTCs";
    [ObservableProperty] string sweepButtonText = "Run sweep";

    /// <summary>
    /// Ask for every mode 01 PID whether the mask claims it or not. Off by default because that is the
    /// wrong way to talk to a vehicle - but it is the only way to catch an ECU whose mask under-reports
    /// what it will actually answer, which does happen.
    /// </summary>
    [ObservableProperty] bool probeUnsupported;

    partial void OnIsSweepingChanged(bool value)
        => this.SweepButtonText = value ? "Cancel" : "Run sweep";

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

    partial void OnSearchChanged(string value) => this.ApplyFilter();

    public void OnAppearing()
    {
        this.ApplyFilter();
        _ = this.ConnectAndSweep();
    }

    public void OnDisappearing()
    {
        this.sweepCts?.Cancel();
        _ = this.Disconnect();
    }

    [RelayCommand]
    Task Sweep() => this.IsSweeping ? this.CancelSweep() : this.ConnectAndSweep();

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
            this.Status = "Not connected";
            return;
        }

        var run = new ObdCommandRun(ObdCommandCatalog.ClearDtcs());
        try
        {
            await this.Execute(run, CancellationToken.None);
            this.Status = $"Clear DTCs — {run.Value}";
        }
        catch (Exception ex)
        {
            this.Status = $"Clear failed: {ex.Message}";
        }
    }

    async Task ConnectAndSweep()
    {
        if (this.Device == null)
        {
            this.Status = "No adapter selected — pick one on the Scan tab";
            return;
        }

        // Set before the first await: connecting takes long enough to tap the button again, and a
        // second connection to the same adapter is not something the first one survives.
        if (this.IsSweeping)
            return;

        this.IsSweeping = true;

        try
        {
            if (this.connection is not { IsConnected: true })
            {
                this.Status = "Connecting...";
                var transport = new BleObdTransport(this.Device, config);
                this.connection = new ObdConnection(transport);
                await this.connection.Connect();

                this.IsConnected = true;
                this.AdapterInfo = (this.connection as ObdConnection)?.DetectedAdapter?.RawIdentifier ?? "Unknown adapter";
            }

            this.sweepCts = new CancellationTokenSource();
            await this.RunSweep(this.sweepCts.Token);
        }
        catch (OperationCanceledException)
        {
            this.Status = "Sweep cancelled";
        }
        catch (Exception ex)
        {
            this.Status = $"Error: {ex.Message}";
            this.IsConnected = false;
        }
        finally
        {
            this.IsSweeping = false;
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
        this.Status = "Walking the mode 01 support mask...";
        var supportedPids = await this.WalkMask(ObdCommandCatalog.SupportGroup, SupportedPidsCommand.BlockPids, ct);

        this.Status = "Walking the mode 06 monitor mask...";
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
                    this.Status = "No freeze frame stored — skipping mode 02";

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

            this.Status = $"{run.Request} — {run.Name}";
            await this.Execute(run, ct);
        }

        this.Status = this.Summary;
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

    async Task Disconnect()
    {
        if (this.connection != null)
        {
            await this.connection.Disconnect();
            await this.connection.DisposeAsync();
            this.connection = null;
        }
        this.IsConnected = false;
    }

    public void Dispose()
    {
        this.sweepCts?.Cancel();
        this.sweepCts?.Dispose();
    }
}
