using Sample.Maui.Emulator;
using Shiny;

namespace Sample.Maui;

/// <summary>
/// The Adapter tab: what the emulator is advertising, who is talking to it, and every command it has
/// answered.
/// </summary>
[ShellMap<HostPage>(registerRoute: false)]
public partial class HostViewModel(ObdHostService host, ObdEmulatorState state) : ObservableObject
{
    public ObdHostService Host => host;

    public ObdEmulatorState State => state;

    public string BleSummary
        => $"Service {host.Configuration.ServiceUuid} · notify {host.Configuration.NotifyCharacteristicUuid} · write {host.Configuration.WriteCharacteristicUuid}";

    public string MdnsSummary
        => $"{host.Configuration.MdnsInstanceName} · {host.Configuration.MdnsServiceType}";

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
