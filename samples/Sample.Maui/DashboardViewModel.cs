using Shiny;
using Shiny.Obd;
using Shiny.Obd.Ble;
using Shiny.Obd.Commands;

namespace Sample.Maui;

[ShellMap<DashboardPage>("dashboard")]
public partial class DashboardViewModel(
    BleObdConfiguration config
) : ObservableObject, IQueryAttributable, IPageLifecycleAware, IDisposable
{
    IObdConnection? connection;
    CancellationTokenSource? pollCts;
    ObdDiscoveredDevice? device;

    [ObservableProperty] string deviceName = "Unknown";
    [ObservableProperty] string status = "Waiting...";
    [ObservableProperty] bool isConnected;
    [ObservableProperty] string connectionButtonText = "Connect";
    [ObservableProperty] string adapterInfo = "--";
    [ObservableProperty] int speed;
    [ObservableProperty] int rpm;
    [ObservableProperty] int coolantTemp;
    [ObservableProperty] double throttle;
    [ObservableProperty] double fuelLevel;
    [ObservableProperty] double engineLoad;

    partial void OnIsConnectedChanged(bool value)
        => this.ConnectionButtonText = value ? "Disconnect" : "Connect";

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Device", out var value) && value is ObdDiscoveredDevice d)
        {
            this.device = d;
            this.DeviceName = d.Name;
        }
    }

    [RelayCommand]
    async Task ToggleConnection()
    {
        if (this.IsConnected)
            await this.DisconnectFromDevice();
        else
            await this.ConnectToDevice();
    }

    public void OnAppearing() => _ = this.ConnectToDevice();
    public void OnDisappearing() => _ = this.DisconnectFromDevice();
    public void OnNavigatingFrom(IDictionary<string, object> parameters) { }

    async Task ConnectToDevice()
    {
        if (this.device == null) return;

        try
        {
            this.Status = "Connecting...";
            var transport = new BleObdTransport(this.device, config);
            this.connection = new ObdConnection(transport);
            await this.connection.Connect();

            this.IsConnected = true;
            var adapter = (this.connection as ObdConnection)?.DetectedAdapter;
            this.AdapterInfo = adapter?.RawIdentifier ?? "Unknown adapter";
            this.Status = "Connected";

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
    }

    async Task PollLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && this.connection?.IsConnected == true)
        {
            try
            {
                this.Speed = await this.connection.Execute(StandardCommands.VehicleSpeed, ct);
                this.Rpm = await this.connection.Execute(StandardCommands.EngineRpm, ct);
                this.CoolantTemp = await this.connection.Execute(StandardCommands.CoolantTemperature, ct);
                this.Throttle = await this.connection.Execute(StandardCommands.ThrottlePosition, ct);
                this.FuelLevel = await this.connection.Execute(StandardCommands.FuelLevel, ct);
                this.EngineLoad = await this.connection.Execute(StandardCommands.CalculatedEngineLoad, ct);
                this.Status = $"Connected — Last update: {DateTime.Now:HH:mm:ss}";
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                this.Status = $"Read error: {ex.Message}";
            }

            try { await Task.Delay(500, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public void Dispose()
    {
        this.pollCts?.Cancel();
        this.pollCts?.Dispose();
    }
}
