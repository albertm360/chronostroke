using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using ChronoStroke.Interop;

namespace ChronoStroke;

/// <summary>
/// Interaction logic for MainWindow.xaml.
/// </summary>
public partial class MainWindow : Window
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
        if (msg != NativeMethods.WM_HOTKEY || wParam.ToInt32() != NativeMethods.HotKeyId)
        {
            return IntPtr.Zero;
        }

        // WM_HOTKEY is posted to this window's message queue, so we are already on the UI
        // thread here — no dispatcher marshalling needed. Toggling is async and a window
        // procedure cannot await, so it is deliberately fire-and-forget.
        handled = true;
        _ = _viewModel.ToggleAsync();
        return IntPtr.Zero;
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (_shutdownComplete)
        {
            return;
        }

        // Stopping the engine is async — it waits for the loop to unwind past the key-up that
        // guarantees nothing is left held down. Closing cannot be awaited, so cancel this close,
        // do the cleanup, then close again for real.
        e.Cancel = true;

        await _viewModel.DisposeEngineAsync();
        _viewModel.ReleaseHotkey();
        _viewModel.SaveSettings();

        // Only remove our hook. The HwndSource itself belongs to the window — disposing it here
        // would tear down the HWND out from under WPF mid-shutdown.
        _source?.RemoveHook(WndProc);
        _source = null;

        _shutdownComplete = true;
        Close();
    }
}
