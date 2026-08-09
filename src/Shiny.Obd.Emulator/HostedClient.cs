namespace Shiny.Obd.Emulator;

/// <summary>A client currently talking to the emulator, over either transport.</summary>
public partial class HostedClient : ObservableObject
{
    public required string Id { get; init; }

    /// <summary>"BLE" or "TCP".</summary>
    public required string Transport { get; init; }

    public required string Address { get; init; }

    public DateTime ConnectedAt { get; } = DateTime.Now;

    [ObservableProperty] int requestCount;
    [ObservableProperty] string lastRequest = "-";

    public string Title => $"{this.Transport} · {this.Address}";

    public string Detail => $"connected {this.ConnectedAt:HH:mm:ss} · {this.RequestCount} request(s) · last: {this.LastRequest}";

    partial void OnRequestCountChanged(int value) => this.OnPropertyChanged(nameof(this.Detail));
    partial void OnLastRequestChanged(string value) => this.OnPropertyChanged(nameof(this.Detail));
}

/// <summary>One line in the emulator's live command log.</summary>
public sealed record ObdLogEntry(DateTime Timestamp, string Transport, string Request, string Response, string Description)
{
    public string Title => $"{this.Timestamp:HH:mm:ss.fff}  {this.Transport}  →  {this.Request}";

    public string Detail => $"{this.Response}    ({this.Description})";
}

/// <summary>
/// What a transport reports back to the app: who is connected and what they asked for. Keeps the BLE
/// and TCP servers free of any knowledge of the UI.
/// </summary>
public interface IObdEmulatorSink
{
    void ClientConnected(HostedClient client);

    void ClientDisconnected(string id);

    void Exchanged(HostedClient client, ObdExchange exchange);

    void Failed(string transport, Exception ex);
}
