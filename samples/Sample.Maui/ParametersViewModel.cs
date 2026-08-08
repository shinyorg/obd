using System.Collections.ObjectModel;
using Sample.Maui.Emulator;
using Shiny;

namespace Sample.Maui;

/// <summary>
/// The Values tab: every command the emulator can answer, and the value it currently answers with.
/// </summary>
[ShellMap<ParametersPage>(registerRoute: false)]
public partial class ParametersViewModel(ObdEmulatorState state) : ObservableObject
{
    public ObservableCollection<ObdParameter> Parameters { get; } = [.. state.Parameters];

    [ObservableProperty] string search = "";

    public string Summary
        => $"{state.Parameters.Count(x => x.IsSupported)} of {state.Parameters.Count} commands supported";

    partial void OnSearchChanged(string value) => this.ApplyFilter();

    [RelayCommand]
    void SupportAll() => this.SetAll(true);

    [RelayCommand]
    void SupportNone() => this.SetAll(false);

    void SetAll(bool supported)
    {
        foreach (var parameter in state.Parameters)
            parameter.IsSupported = supported;

        this.OnPropertyChanged(nameof(this.Summary));
    }

    void ApplyFilter()
    {
        var term = this.Search?.Trim() ?? "";

        // Matching the request hex as well as the name is what makes "010C" or "0902" work - which is
        // how you look a PID up when you are staring at a log rather than a UI.
        var matches = state.Parameters.Where(x =>
            term.Length == 0 ||
            x.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            x.Request.Contains(term, StringComparison.OrdinalIgnoreCase)
        );

        this.Parameters.Clear();
        foreach (var parameter in matches)
            this.Parameters.Add(parameter);
    }
}
