using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Interop;
using ChronoStroke.Interop;

namespace ChronoStroke;

/// <summary>
/// Interaction logic for MainWindow.xaml.
/// </summary>
[SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable",
    Justification = "A Window's lifetime is owned by WPF, which never calls IDisposable on it. " +
                    "OnClosing is where a window releases what it owns, and it disposes the view " +
                    "model there — before the window is allowed to finish closing.")]
internal partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private HwndSource? _source;
    private bool _shutdownComplete;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // This is the earliest point at which the window has a real HWND, which RegisterHotKey
        // requires. Doing it in the constructor would hand it IntPtr.Zero.
        var handle = new WindowInteropHelper(this).Handle;

        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);

        _viewModel.AttachWindow(handle);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Compared as nint rather than via wParam.ToInt32(), which throws OverflowException
        // when the high 32 bits are set. Only WM_HOTKEY reaches it today, where the value is a
        // small id, but a comparison that cannot throw costs nothing.
        if (msg != NativeMethods.WM_HOTKEY || wParam != NativeMethods.HotKeyId)
        {
            return IntPtr.Zero;
        }

        // WM_HOTKEY is posted to this window's message queue, so we are already on the UI
        // thread here — no dispatcher marshalling needed. Toggling is async and a window
        // procedure cannot await, so the call cannot be awaited here.
        handled = true;
        ToggleFromHotKey();
        return IntPtr.Zero;
    }

    /// <summary>
    /// async void deliberately. Discarding the Task instead (<c>_ = ToggleAsync()</c>) would
    /// swallow any failure silently and leave the hotkey looking dead; an async void method
    /// started on the UI thread posts its exception to the dispatcher, where App's handler
    /// reports it.
    /// </summary>
    private async void ToggleFromHotKey() => await _viewModel.ToggleAsync();

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_shutdownComplete)
        {
            // Second pass. base.OnClosing raises the public Closing event, so it must run once
            // per close, not once per pass through this method.
            base.OnClosing(e);
            return;
        }

        base.OnClosing(e);

        // Stopping the engine is async — it waits for the loop to unwind past the key-up that
        // guarantees nothing is left held down. Closing cannot be awaited, so cancel this close,
        // do the cleanup, then close again for real.
        e.Cancel = true;

        // Order matters. Awaiting below keeps pumping the dispatcher, so anything still able to
        // reach the view model can start the engine back up during teardown and leave a key held
        // down as the process exits. Make the window inert first, then tear down.
        //
        // Only remove our hook. The HwndSource itself belongs to the window — disposing it here
        // would tear down the HWND out from under WPF mid-shutdown.
        _source?.RemoveHook(WndProc);
        _source = null;
        IsEnabled = false;              // no more Start button either

        // Everything the view model owns comes down in its own order, not one dictated here.
        await _viewModel.DisposeAsync();

        _shutdownComplete = true;

        // Post the real close rather than calling it here. WPF keeps the window flagged as
        // closing for the whole synchronous OnClosing call and throws from any Close() that
        // arrives while the flag is set — and the await above is not a guarantee that we have
        // left that call: with the engine already stopped, DisposeAsync completes without ever
        // yielding, so execution reaches this line still on the original stack. Queuing it
        // means it runs once the dispatcher has unwound the close we are inside.
        await Dispatcher.InvokeAsync(Close);
    }
}
