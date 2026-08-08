using Sample.Maui.Emulator;

namespace Sample.Maui;

public partial class App : Application
{
    readonly ObdHostService host;

    public App(ObdHostService host)
    {
        this.host = host;
        this.InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // The emulator is meant to be live as soon as the app is, so this does not wait for the
        // Adapter tab to be opened. Start reports its own failures through the service's status
        // properties rather than throwing into window creation.
        _ = this.host.Start();

        return new Window(new AppShell());
    }
}
