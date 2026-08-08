namespace Sample.Maui;

public partial class App : Application
{
    public App()
    {
        this.InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // A scan tool is read while it is being watched and nothing is being touched - a screen that
        // sleeps mid-drive is the one thing that makes this app unusable in a car.
        DeviceDisplay.Current.KeepScreenOn = true;

        // The emulator is deliberately *not* started here. Starting it at launch puts a BLE peripheral
        // and a TCP listener up on every device that opens the app, including the one that is only
        // here to talk to a real adapter. Start it from the Adapter tab when that is what you want.
        return new Window(new AppShell());
    }
}
