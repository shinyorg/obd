using Shiny.Obd.Emulator;
using Shiny;

namespace Sample.Maui;

/// <summary>
/// The Drive tab: picks a scenario and plays it into the emulator, so the values a client polls move
/// the way a driven vehicle's do instead of sitting where the Values tab left them.
/// </summary>
[ShellMap<DrivePage>(registerRoute: false)]
public partial class DriveViewModel(DrivingScenarioPlayer player, ObdEmulatorState state) : ObservableObject
{
    public DrivingScenarioPlayer Player => player;

    /// <summary>The scenario says how it is driven; the vehicle chosen on the Adapter tab says what is being driven.</summary>
    public ObdEmulatorState State => state;

    [RelayCommand]
    void Toggle() => player.Toggle();

    [RelayCommand]
    void Restart()
    {
        player.Stop();
        player.Start();
    }
}
