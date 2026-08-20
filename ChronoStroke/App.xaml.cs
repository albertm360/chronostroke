using System.Windows;
using System.Windows.Threading;

namespace ChronoStroke;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>Stops a crash inside the crash handler from looping the dialog forever.</summary>
    private bool _crashing;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Without a handler here, any exception reaching the dispatcher kills the process on the
        // spot. For this app that is worse than it sounds: the repeat loop may be between its
        // key-down and key-up, and a process that dies there leaves the key held down in whatever
        // window had focus. Handling it lets us shut down through MainWindow.OnClosing, which
        // unwinds the loop past the key-up first.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // A faulted Task whose exception nobody observed is finalized quietly in .NET, but the
        // fire-and-forget toggle at MainWindow.WndProc is exactly that shape. Observe them so a
        // failure cannot be lost without trace.
        TaskScheduler.UnobservedTaskException += (_, args) => args.SetObserved();
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
