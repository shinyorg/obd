namespace Sample.Maui;

public partial class ScanPage : ContentPage
{
    public ScanPage(ScanViewModel viewModel)
    {
        this.InitializeComponent();
        this.BindingContext = viewModel;
    }
}
