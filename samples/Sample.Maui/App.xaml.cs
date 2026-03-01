namespace Sample.Maui;

public partial class App : Application
{
    public App()
    {
    }

    protected override Window CreateWindow(IActivationState? activationState)
        => new Window(new AppShell());
}
