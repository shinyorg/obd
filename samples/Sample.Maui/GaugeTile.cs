namespace Sample.Maui;

/// <summary>One dial on the dashboard, and the last thing the vehicle said for it.</summary>
public partial class GaugeTile(GaugeDefinition definition) : ObservableObject
{
    public GaugeDefinition Definition => definition;

    [ObservableProperty] double value = definition.Minimum;

    /// <summary>
    /// False until the vehicle answers, and again whenever it stops. A dial has no way to draw "absent",
    /// and a needle resting on its peg reads as a real zero - so the page dims it instead.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoReading))]
    bool hasReading;

    public bool NoReading => !this.HasReading;

    public void Clear()
    {
        this.HasReading = false;
        this.Value = definition.Minimum;
    }
}

/// <summary>One chip in the gauge picker.</summary>
public partial class GaugeOption(GaugeDefinition definition) : ObservableObject
{
    public GaugeDefinition Definition => definition;
    public string Title => definition.Title;

    [ObservableProperty] bool isSelected;

    /// <summary>False for an unselected chip once the dashboard is full.</summary>
    [ObservableProperty] bool canSelect = true;
}
