using Shiny.Maui.Controls;

namespace Sample.Maui;

/// <summary>
/// A <see cref="Gauge"/> configured from a <see cref="GaugeDefinition"/>, so a data template can stamp out
/// any dial in the catalog. <see cref="Gauge.Ranges"/> is a get-only collection and cannot be bound,
/// which is the reason this exists rather than binding each scale property in XAML.
/// </summary>
public class ObdGauge : Gauge
{
    public static readonly BindableProperty DefinitionProperty = BindableProperty.Create(
        nameof(Definition),
        typeof(GaugeDefinition),
        typeof(ObdGauge),
        propertyChanged: (bindable, _, value) => ((ObdGauge)bindable).Apply(value as GaugeDefinition)
    );

    public GaugeDefinition? Definition
    {
        get => (GaugeDefinition?)this.GetValue(DefinitionProperty);
        set => this.SetValue(DefinitionProperty, value);
    }

    void Apply(GaugeDefinition? definition)
    {
        this.Ranges.Clear();
        if (definition == null)
            return;

        this.Minimum = definition.Minimum;
        this.Maximum = definition.Maximum;
        this.MajorTickInterval = definition.MajorTickInterval;
        this.MinorTicksPerMajor = definition.MinorTicksPerMajor;
        this.ReadoutDecimals = definition.ReadoutDecimals;
        this.Title = definition.Title;
        this.Units = definition.Units;

        foreach (var band in definition.Bands)
            this.Ranges.Add(new GaugeRange { Start = band.Start, End = band.End, Color = band.Color });
    }
}
