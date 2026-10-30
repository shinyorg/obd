using System.Collections.ObjectModel;
using Shiny;
using Shiny.Obd;
using Shiny.Obd.Bluetooth;

namespace Sample.Maui;

[ShellMap<ScanPage>(registerRoute: false)]
public partial class ScanViewModel(
    IObdDeviceScanner scanner,
    INavigator navigator,
    // Android only - see MauiProgram. Null on iOS, where classic Bluetooth is not available.
    BluetoothObdDeviceScanner? classicScanner = null
) : ObservableObject, IPageLifecycleAware
{
    CancellationTokenSource? scanCts;

    public ObservableCollection<ObdDiscoveredDevice> Devices { get; } = new();

    [ObservableProperty]
    bool isScanning;

    [ObservableProperty]
    string scanButtonText = "Start Scan";

    [RelayCommand]
    void ToggleScan()
    {
        if (this.IsScanning)
            this.StopScan();
        else
            this.StartScan();
    }

    [RelayCommand]
    Task SelectDevice(ObdDiscoveredDevice device) => navigator.NavigateTo<DashboardViewModel>(vm => vm.Device = device);

    void StartScan()
    {
        this.Devices.Clear();
        this.IsScanning = true;
        this.ScanButtonText = "Stop Scan";
        this.scanCts = new CancellationTokenSource();

        _ = scanner.Scan(this.OnDeviceFound, this.scanCts.Token);

        // Paired classic adapters show up immediately; unpaired ones as the inquiry hears them.
        if (classicScanner != null)
            _ = classicScanner.Scan(this.OnDeviceFound, this.scanCts.Token);
    }

    void OnDeviceFound(ObdDiscoveredDevice device)
        => MainThread.BeginInvokeOnMainThread(() => this.Devices.Add(device));

    void StopScan()
    {
        this.scanCts?.Cancel();
        this.scanCts?.Dispose();
        this.scanCts = null;
        this.IsScanning = false;
        this.ScanButtonText = "Start Scan";
    }

    public void OnAppearing() { }

    public void OnDisappearing() => this.StopScan();
}
