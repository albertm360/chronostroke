using System.Windows;
using System.Windows.Threading;

namespace ChronoStroke;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
internal partial class App : Application
{
    /// <summary>Stops a crash inside the crash handler from looping the dialog forever.</summary>
    private bool _crashing;

    /// <summary>Held for the lifetime of the process; released in <see cref="OnExit"/>.</summary>
    private SingleInstance? _instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Before anything else, and in particular before a window exists to register a hotkey
        // from. Left to run in full, a second copy loses the RegisterHotKey race and reports the
        // combination as held by "another application" — which is ChronoStroke. It can still be
        // started with the mouse from there, at which point two copies are injecting and only
        // one of them has a hotkey that can stop anything.
        if (!SingleInstance.TryAcquire(out _instance))
        {
            SingleInstance.ActivateExisting();
            Shutdown();
            return;
        }

        // Without a handler here, any exception reaching the dispatcher kills the process on the
        // spot. For this app that is worse than it sounds: the repeat loop may be between its
        // key-down and key-up, and a process that dies there leaves the key held down in whatever
        // window had focus. Handling it lets us shut down through MainWindow.OnClosing, which
        // unwinds the loop past the key-up first.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // There is deliberately no TaskScheduler.UnobservedTaskException handler. One used to sit
        // here calling SetObserved() and nothing else, above a comment claiming it existed so a
        // failure could not be lost without trace — which is the opposite of what an empty
        // handler does. It also guarded against nothing: since .NET Core an unobserved task
        // exception does not tear down the process, and the case the comment named is already
        // handled properly. MainWindow.ToggleFromHotKey is async void precisely so its exceptions
        // reach the dispatcher and the handler above, which means they are never unobserved.

        // Shown here rather than through App.xaml's StartupUri, because Shutdown above does not
        // call the startup window off. Measured both ways with the constructor logging, running
        // a second copy against a first: with StartupUri the second copy still constructs
        // MainWindow — and with it a MainViewModel, which reads settings.json and arms the save
        // timer — before the shutdown catches up. It gets no further, so nothing appears on
        // screen and no hotkey is registered, but it is a whole window built for a process that
        // is already leaving. Showing the window here instead means the second copy builds
        // nothing: one constructor call across both processes rather than two.
        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Releases the claim so the next copy starts normally. Not strictly required — the kernel
        // closes the handle when the process ends either way — but leaving it to process teardown
        // would mean the one path that tidies up is the one nobody wrote.
        _instance?.Dispose();

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        if (_crashing)
        {
            return;
        }

        _crashing = true;

        MessageBox.Show(
            $"ChronoStroke hit an unexpected error and will close.\n\n{e.Exception}",
            "ChronoStroke",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // Shutdown closes the main window, so the engine still gets its ordered teardown.
        Shutdown(1);
    }
}
