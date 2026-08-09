using Shiny.Obd.Emulator;
using Shiny;

namespace Sample.Maui;

/// <summary>
/// The Faults tab: the fault memory and adapter identity a client sees. Everything here feeds the
/// derived PIDs (01 and 41) and modes 03, 07, 0A and 04.
/// </summary>
[ShellMap<FaultsPage>(registerRoute: false)]
public partial class FaultsViewModel(ObdEmulatorState state) : ObservableObject
{
    public ObdEmulatorState State => state;

    [ObservableProperty] string newStoredDtc = "P0128";
    [ObservableProperty] string newPendingDtc = "P0442";
    [ObservableProperty] string newPermanentDtc = "P0420";
    [ObservableProperty] string error = "";

    public bool HasError => this.Error.Length > 0;

    partial void OnErrorChanged(string value) => this.OnPropertyChanged(nameof(this.HasError));

    [RelayCommand]
    void AddStored() => this.Add(this.NewStoredDtc, state.StoredDtcs);

    [RelayCommand]
    void AddPending() => this.Add(this.NewPendingDtc, state.PendingDtcs);

    [RelayCommand]
    void AddPermanent() => this.Add(this.NewPermanentDtc, state.PermanentDtcs);

    [RelayCommand]
    void RemoveStored(string code) => this.Remove(code, state.StoredDtcs);

    [RelayCommand]
    void RemovePending(string code) => this.Remove(code, state.PendingDtcs);

    [RelayCommand]
    void RemovePermanent(string code) => this.Remove(code, state.PermanentDtcs);

    [RelayCommand]
    void ClearAll() => state.ClearDtcs();

    void Add(string? code, IList<string> list)
    {
        var trimmed = code?.Trim().ToUpperInvariant() ?? "";

        // Validate by encoding it: if the two DTC bytes cannot be produced, no client would ever read
        // this code back, and a silently dropped entry is worse than a message.
        if (!ObdEmulatorState.TryEncodeDtc(trimmed, out _, out _))
        {
            this.Error = $"'{trimmed}' is not a DTC. Use a letter P, C, B or U, then 0-3, then three hex digits (eg P0301).";
            return;
        }

        this.Error = "";
        if (!list.Contains(trimmed))
            list.Add(trimmed);

        state.RefreshDerived();
    }

    void Remove(string code, IList<string> list)
    {
        list.Remove(code);
        state.RefreshDerived();
    }
}
