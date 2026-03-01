using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Shiny.Obd;
using Shiny.Obd.Ble;
using Shiny.Obd.Commands;

namespace Sample.Maui;

[QueryProperty(nameof(Device), "Device")]
public class DashboardViewModel : INotifyPropertyChanged
{
    readonly BleObdConfiguration config;
    IObdConnection? connection;
    CancellationTokenSource? pollCts;

    public DashboardViewModel(BleObdConfiguration config)
    {
        this.config = config;

        this.ToggleConnectionCommand = new Command(async () =>
        {
            if (this.IsConnected)
                await this.DisconnectFromDevice();
            else
                await this.ConnectToDevice();
        });
    }

    ObdDiscoveredDevice? device;
    public ObdDiscoveredDevice? Device
    {
        get => this.device;
        set
        {
            this.SetProperty(ref this.device, value);
            this.DeviceName = value?.Name ?? "Unknown";
            _ = this.ConnectToDevice();
        }
    }

    public ICommand ToggleConnectionCommand { get; }

    string deviceName = "Unknown";
    public string DeviceName
    {
        get => this.deviceName;
        set => this.SetProperty(ref this.deviceName, value);
    }

    string status = "Connecting...";
    public string Status
    {
        get => this.status;
        set => this.SetProperty(ref this.status, value);
    }

    bool isConnected;
    public bool IsConnected
    {
        get => this.isConnected;
        set
        {
            this.SetProperty(ref this.isConnected, value);
            this.ConnectionButtonText = value ? "Disconnect" : "Connect";
        }
    }

    string connectionButtonText = "Connect";
    public string ConnectionButtonText
    {
        get => this.connectionButtonText;
        set => this.SetProperty(ref this.connectionButtonText, value);
    }

    string adapterInfo = "--";
    public string AdapterInfo
    {
        get => this.adapterInfo;
        set => this.SetProperty(ref this.adapterInfo, value);
    }

    int speed;
    public int Speed
    {
        get => this.speed;
        set => this.SetProperty(ref this.speed, value);
    }

    int rpm;
    public int Rpm
    {
        get => this.rpm;
        set => this.SetProperty(ref this.rpm, value);
    }

    int coolantTemp;
    public int CoolantTemp
    {
        get => this.coolantTemp;
        set => this.SetProperty(ref this.coolantTemp, value);
    }

    double throttle;
    public double Throttle
    {
        get => this.throttle;
        set => this.SetProperty(ref this.throttle, value);
    }

    double fuelLevel;
    public double FuelLevel
    {
        get => this.fuelLevel;
        set => this.SetProperty(ref this.fuelLevel, value);
    }

    double engineLoad;
    public double EngineLoad
    {
        get => this.engineLoad;
        set => this.SetProperty(ref this.engineLoad, value);
    }

    async Task ConnectToDevice()
    {
        if (this.device == null) return;

        try
        {
            this.Status = "Connecting...";
            var transport = new BleObdTransport(this.device, this.config);
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

    public event PropertyChangedEventHandler? PropertyChanged;
    void SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
