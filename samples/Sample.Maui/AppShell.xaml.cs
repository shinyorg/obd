namespace Sample.Maui;

public partial class AppShell : Shell
{
    public AppShell()
    {
        this.InitializeComponent();
        Routing.RegisterRoute("dashboard", typeof(DashboardPage));
    }
}
