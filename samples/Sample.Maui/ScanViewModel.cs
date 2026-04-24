using System.Collections.ObjectModel;
using Shiny;
using Shiny.Obd;

namespace Sample.Maui;

[ShellMap<ScanPage>(registerRoute: false)]
public partial class ScanViewModel(
    IObdDeviceScanner scanner,
    INavigator navigator
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

        _ = scanner.Scan(
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

    public void OnAppearing() { }

    public void OnDisappearing() => this.StopScan();
}
