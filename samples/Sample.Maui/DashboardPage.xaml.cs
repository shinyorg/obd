namespace Sample.Maui;

public partial class DashboardPage : ContentPage
{
    public DashboardPage(DashboardViewModel viewModel)
    {
        this.InitializeComponent();
        this.BindingContext = viewModel;
    }
}
