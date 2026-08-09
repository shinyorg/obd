using Shiny.Obd.Emulator;
using Shiny;

namespace Sample.Maui;

/// <summary>
/// The Adapter tab: what the emulator is advertising, who is talking to it, and every command it has
/// answered.
/// </summary>
[ShellMap<HostPage>(registerRoute: false)]
public partial class HostViewModel(ObdEmulatorHost host, ObdEmulatorState state) : ObservableObject
{
    public ObdEmulatorHost Host => host;

    public ObdEmulatorState State => state;

    /// <summary>
    /// Each transport reports its own status and detail lines, so the page renders whatever is
    /// registered rather than hard-coding a card for BLE and one for TCP.
    /// </summary>
    public string HostButtonText => host.IsRunning ? "Stop hosting" : "Start hosting";

    [RelayCommand]
    async Task ToggleHost()
    {
        if (host.IsRunning)
            await host.Stop();
        else
            await host.Start();

        this.OnPropertyChanged(nameof(this.HostButtonText));
    }

    [RelayCommand]
    void ClearLog() => host.ClearLog();
}
