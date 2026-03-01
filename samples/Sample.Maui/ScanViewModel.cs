using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Shiny.Obd;

namespace Sample.Maui;

public class ScanViewModel : INotifyPropertyChanged
{
    readonly IObdDeviceScanner scanner;
    CancellationTokenSource? scanCts;

    public ScanViewModel(IObdDeviceScanner scanner)
    {
        this.scanner = scanner;

        this.ToggleScanCommand = new Command(() =>
        {
            if (this.IsScanning)
                this.StopScan();
            else
                this.StartScan();
        });

        this.SelectDeviceCommand = new Command<ObdDiscoveredDevice>(async device =>
        {
            this.StopScan();
            await Shell.Current.GoToAsync("dashboard", new Dictionary<string, object>
            {
                ["Device"] = device
            });
        });
    }

    public ObservableCollection<ObdDiscoveredDevice> Devices { get; } = new();
    public ICommand ToggleScanCommand { get; }
    public ICommand SelectDeviceCommand { get; }

    bool isScanning;
    public bool IsScanning
    {
        get => this.isScanning;
        set => this.SetProperty(ref this.isScanning, value);
    }

    string scanButtonText = "Start Scan";
    public string ScanButtonText
    {
        get => this.scanButtonText;
        set => this.SetProperty(ref this.scanButtonText, value);
    }

    void StartScan()
    {
        this.Devices.Clear();
        this.IsScanning = true;
        this.ScanButtonText = "Stop Scan";
        this.scanCts = new CancellationTokenSource();

        _ = this.scanner.Scan(
            device => MainThread.BeginInvokeOnMainThread(() => this.Devices.Add(device)),
            this.scanCts.Token
        );
    }

    void StopScan()
    {
        this.scanCts?.Cancel();
        this.scanCts?.Dispose();
        this.scanCts = null;
        this.IsScanning = false;
        this.ScanButtonText = "Start Scan";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
