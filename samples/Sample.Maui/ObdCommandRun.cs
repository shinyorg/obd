using Shiny.Obd;
using Shiny.Obd.Commands;

namespace Sample.Maui;

public enum ObdCommandState
{
    /// <summary>Not asked yet.</summary>
    Pending,

    /// <summary>On the wire right now.</summary>
    Running,

    /// <summary>Answered, and the answer parsed.</summary>
    Answered,

    /// <summary>The support mask says the vehicle does not have it, so it was never asked.</summary>
    Unsupported,

    /// <summary>Asked, and the vehicle said NO DATA.</summary>
    NoData,

    /// <summary>Asked, and something went wrong - a parse failure, a timeout, a garbled reply.</summary>
    Failed
}

/// <summary>
/// One row of the sweep: a command, and what came back when it was asked.
/// </summary>
public partial class ObdCommandRun(ObdCommandEntry entry) : ObservableObject
{
    public ObdCommandEntry Entry => entry;

    public string Group => entry.Group;
    public string Name => entry.Name;
    public string Request => entry.Request;
    public string? Note => entry.Note;
    public bool HasNote => entry.Note is { Length: > 0 };

    [ObservableProperty] ObdCommandState state;
    [ObservableProperty] string value = "";
    [ObservableProperty] int elapsedMs;

    public bool HasValue => this.Value.Length > 0;

    public string StateText => this.State switch
    {
        ObdCommandState.Pending => "not asked",
        ObdCommandState.Running => "asking...",
        ObdCommandState.Answered => $"{this.ElapsedMs} ms",
        ObdCommandState.Unsupported => "not supported",
        ObdCommandState.NoData => "no data",
        _ => "failed"
    };

    public Color StateColor => this.State switch
    {
        ObdCommandState.Answered => Colors.SeaGreen,
        ObdCommandState.Failed => Colors.IndianRed,
        ObdCommandState.Running => Colors.DodgerBlue,
        _ => Colors.Gray
    };

    partial void OnStateChanged(ObdCommandState oldValue, ObdCommandState newValue)
    {
        this.OnPropertyChanged(nameof(this.StateText));
        this.OnPropertyChanged(nameof(this.StateColor));
    }

    partial void OnElapsedMsChanged(int oldValue, int newValue)
        => this.OnPropertyChanged(nameof(this.StateText));

    partial void OnValueChanged(string? oldValue, string newValue)
        => this.OnPropertyChanged(nameof(this.HasValue));

    public void Reset()
    {
        this.State = ObdCommandState.Pending;
        this.Value = "";
        this.ElapsedMs = 0;
    }
}

/// <summary>
/// Renders a parsed command result for display.
/// </summary>
/// <remarks>
/// The point of the sweep is to show what a caller actually receives, so the interesting types get a
/// shape that reflects how you would read them rather than falling through to a record's generated
/// <c>ToString</c>.
/// </remarks>
public static class ObdValueFormatter
{
    public static string Format(object? value) => value switch
    {
        null => "(none)",
        string s => s.Length == 0 ? "(empty)" : s,
        bool b => b ? "acknowledged" : "refused",
        double d => d.ToString("0.###"),
        float f => f.ToString("0.###"),
        TimeSpan t => t.ToString(),

        MonitorStatus status => FormatMonitorStatus(status),
        InUsePerformanceTracking tracking => FormatTracking(tracking),
        IReadOnlyList<OnBoardTestResult> tests => Join(tests.Select(FormatTest)),

        // A byte list is always a PID or MID list here, and hex is how you would look it up.
        IEnumerable<byte> bytes => Join(bytes.Select(x => x.ToString("X2"))),
        IEnumerable<string> list => Join(list),
        System.Collections.IEnumerable list => Join(list.Cast<object?>().Select(Format)),

        _ => value.ToString() ?? ""
    };

    static string FormatMonitorStatus(MonitorStatus status)
    {
        var readiness = status.IsReadyForInspection switch
        {
            true => "ready for inspection",
            false => $"incomplete: {Join(status.Incomplete.Select(x => x.Monitor.ToString()))}",
            null => "no monitors reported"
        };
        return $"MIL {(status.MilOn ? "on" : "off")}, {status.DtcCount} DTC(s), {status.Ignition}, {readiness}";
    }

    static string FormatTracking(InUsePerformanceTracking tracking)
    {
        var ratios = tracking.Monitors.Select(x =>
            $"{x.Monitor ?? "unnamed"} {x.Completions}/{x.Conditions}"
        );
        return $"conditions {tracking.MonitoringConditions}, ignition cycles {tracking.IgnitionCycles} — {Join(ratios)}";
    }

    static string FormatTest(OnBoardTestResult test)
    {
        var name = test.Monitor ?? $"MID {test.Mid:X2}";
        var verdict = test.Passed switch
        {
            true => "pass",
            false => "FAIL",
            null => "unscaled"
        };

        // Without a scaling table entry the raw counts are all there is to show, and showing them as
        // though they were volts or grams would be worse than showing them raw.
        return test.Value is { } scaled
            ? $"{name} test {test.TestId:X2}: {scaled:0.###}{test.Unit} in {test.Minimum:0.###}-{test.Maximum:0.###} ({verdict})"
            : $"{name} test {test.TestId:X2}: {test.RawValue} in {test.RawMinimum}-{test.RawMaximum} ({verdict})";
    }

    static string Join(IEnumerable<string> values)
    {
        var joined = String.Join(", ", values);
        return joined.Length == 0 ? "(none)" : joined;
    }
}
