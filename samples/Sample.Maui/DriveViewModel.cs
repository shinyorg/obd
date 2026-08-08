using Sample.Maui.Emulator;
using Shiny;

namespace Sample.Maui;

/// <summary>
/// The Drive tab: picks a scenario and plays it into the emulator, so the values a client polls move
/// the way a driven vehicle's do instead of sitting where the Values tab left them.
/// </summary>
[ShellMap<DrivePage>(registerRoute: false)]
public partial class DriveViewModel(DrivingScenarioPlayer player) : ObservableObject
{
    public DrivingScenarioPlayer Player => player;

    [RelayCommand]
    void Toggle() => player.Toggle();

    [RelayCommand]
    void Restart()
    {
        player.Stop();
        player.Start();
    }
}
