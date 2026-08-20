using System.Globalization;
using System.Windows.Threading;
using ChronoStroke.Interop;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChronoStroke;

internal sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    /// <summary>
    /// Guard rail. Below this the machine is flooded with input faster than most windows can
    /// drain it, and recovering means killing the process.
    /// </summary>
    public const int MinIntervalMs = 50;

    public const int MaxIntervalMs = 60_000;

    private readonly RepeatEngine _engine = new();

    /// <summary>
    /// The dispatcher of the thread that built this view model — the UI thread. Captured rather
    /// than reached for through Application.Current so the view model does not depend on a
    /// running WPF application to marshal its own events.
    /// </summary>
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    private GlobalHotKey? _hotKey;

    /// <summary>Last combination that registered successfully — the target to revert to.</summary>
    private KeyCombo _lastGoodHotkey;

    /// <summary>Guards against re-entering ApplyHotkey when a failure reverts the property.</summary>
    private bool _applyingHotkey;

    private string? _hotkeyRegistrationError;

    /// <summary>Last interval the box held that parsed and passed the guard rails.</summary>
    private int _lastValidIntervalMs = AppSettings.Default.IntervalMs;

    /// <summary>What is currently on disk, so an unchanged configuration is not rewritten.</summary>
    private AppSettings? _lastSaved;

    /// <summary>Suppresses saving while settings are being applied from disk.</summary>
    private bool _loading;

    /// <summary>Shortest gap between two accepted hotkey toggles.</summary>
    private const int HotkeyDebounceMs = 250;

    private long _lastToggleTicks;

    public MainViewModel()
    {
        // The loop runs on the thread pool, so these arrive off the UI thread.
        _engine.SendFailed += (_, message) => OnUiThread(() => Status = message);
        _engine.WaitingForReleaseChanged += (_, waiting) => OnUiThread(() =>
            Status = waiting
                ? $"Waiting for you to let go of {HotkeyCombo.DisplayName}…"
                : RunningStatus());

        ApplySettings(SettingsStore.Load());
    }

    private void OnUiThread(Action action)
    {
        // Engine events arrive on thread-pool threads and have to be marshalled. Anything
        // already on the right thread runs inline instead of queueing behind the current
        // message, which keeps the ordering obvious.
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        // InvokeAsync on a dispatcher that has already shut down abandons the operation quietly
        // rather than throwing — which is the behaviour we want during teardown.
        _dispatcher.InvokeAsync(action);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyError), nameof(HasHotkeyError))]
    [NotifyPropertyChangedFor(nameof(HotkeyWarning), nameof(HasHotkeyWarning))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial KeyCombo SendCombo { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyError), nameof(HasHotkeyError))]
    [NotifyPropertyChangedFor(nameof(HotkeyWarning), nameof(HasHotkeyWarning))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial KeyCombo HotkeyCombo { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IntervalError), nameof(HasIntervalError))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string IntervalText { get; set; } = "250";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "Pick a key to send.";

    /// <summary>Null when the interval is usable, otherwise the reason it is not.</summary>
    public string? IntervalError => ValidateInterval(IntervalText, out _);

    public bool HasIntervalError => IntervalError is not null;

    /// <summary>Settings are locked while the loop runs — the engine captured them at Start.</summary>
    public bool CanEdit => !IsRunning;

    /// <summary>
    /// The sent key and the start/stop hotkey must differ. If they were the same, every injected
    /// keystroke would match our own hotkey registration and toggle the loop back off — the app
    /// would fight itself. Blocking is kinder than letting it misbehave inexplicably.
    /// </summary>
    private string? CollisionError =>
        !SendCombo.IsEmpty && SendCombo == HotkeyCombo
            ? "The hotkey must differ from the key being sent, or the repeated keystroke will trigger it."
            : null;

    public string? HotkeyError => CollisionError ?? _hotkeyRegistrationError;

    public bool HasHotkeyError => HotkeyError is not null;

    /// <summary>
    /// Advisory, not blocking. The two combinations differ, so nothing is wrong yet — but they
    /// share a virtual key, and the modifiers are the only thing keeping them apart. Send F8
    /// with Ctrl+F8 as the hotkey and the configuration works exactly as intended until the
    /// moment the user happens to hold Ctrl for something unrelated, at which point the loop's
    /// own F8 becomes Ctrl+F8 and switches itself off. WaitForTriggerReleaseAsync covers the
    /// start; nothing can cover the middle, so the honest answer is to say so up front.
    /// </summary>
    public string? HotkeyWarning =>
        CollisionError is null && !SendCombo.IsEmpty && !HotkeyCombo.IsEmpty
        && SendCombo.VirtualKey == HotkeyCombo.VirtualKey
            ? $"{HotkeyCombo.DisplayName} and {SendCombo.DisplayName} are the same key with "
              + "different modifiers. Holding a modifier while the loop runs can turn the "
              + "repeated keystroke into the hotkey and stop it."
            : null;

    public bool HasHotkeyWarning => HotkeyWarning is not null;

    // ---------------------------------------------------------------- settings

    private void ApplySettings(AppSettings settings)
    {
        _loading = true;
        try
        {
            SendCombo = settings.SendCombo;
            HotkeyCombo = settings.HotkeyCombo;

            // Clamp rather than trust. The file is plain text in a folder the user can open, so
            // a hand-edited 1 ms interval must not slip past the guard rail the UI enforces.
            _lastValidIntervalMs = Math.Clamp(settings.IntervalMs, MinIntervalMs, MaxIntervalMs);
            IntervalText = _lastValidIntervalMs.ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _loading = false;
        }

        // What is now on screen, so the first edit is compared against it rather than written
        // back unchanged.
        _lastSaved = AppSettings.From(SendCombo, HotkeyCombo, _lastValidIntervalMs);
    }

    /// <summary>
    /// Persists the current configuration. Called on every accepted edit and again on shutdown,
    /// so a crash or a force-quit still leaves the last good settings on disk.
    /// </summary>
    public void SaveSettings()
    {
        if (_loading)
        {
            return;
        }

        var settings = AppSettings.From(SendCombo, HotkeyCombo, _lastValidIntervalMs);

        // The interval box updates its binding on every keystroke, so typing "250" arrives here
        // three times and half-typed values like "2" arrive as invalid ones. Comparing against
        // what was last written collapses that to a single save once the value is usable, and
        // skips the disk entirely while the user is still mid-number.
        if (settings == _lastSaved)
        {
            return;
        }

        var error = SettingsStore.Save(settings);
        if (error is not null)
        {
            Status = error;
            return;
        }

        _lastSaved = settings;
    }

    partial void OnSendComboChanged(KeyCombo value) => SaveSettings();

    partial void OnIntervalTextChanged(string value)
    {
        // Half-typed values never become the saved interval; the last usable one stands until
        // the box holds another.
        if (ValidateInterval(value, out var interval) is null)
        {
            _lastValidIntervalMs = interval;
        }

        SaveSettings();
    }

    // ------------------------------------------------------------------ hotkey

    /// <summary>
    /// Called once the window has an HWND. RegisterHotKey needs a real handle, which does not
    /// exist until the window is sourced.
    /// </summary>
    public void AttachWindow(IntPtr windowHandle)
    {
        _hotKey = new GlobalHotKey(windowHandle);
        ApplyHotkey();
    }

    partial void OnHotkeyComboChanged(KeyCombo value)
    {
        ApplyHotkey();
        SaveSettings();
    }

    private void ApplyHotkey()
    {
        if (_hotKey is null || _applyingHotkey)
        {
            return;
        }

        _applyingHotkey = true;
        try
        {
            if (_hotKey.TryRegister(HotkeyCombo, out var error))
            {
                _lastGoodHotkey = HotkeyCombo;
                _hotkeyRegistrationError = null;
            }
            else
            {
                _hotkeyRegistrationError = error;

                // Roll back to whatever last worked and put that registration back in place, so
                // a rejected choice never leaves the app with no working hotkey at all.
                if (!_lastGoodHotkey.IsEmpty && _lastGoodHotkey != HotkeyCombo)
                {
                    HotkeyCombo = _lastGoodHotkey;
                    if (_hotKey.TryRegister(_lastGoodHotkey, out _))
                    {
                        // The box has just gone back to showing the old combination, so leaving
                        // the error in place would park a permanent-looking complaint about
                        // Ctrl+Shift+P directly beneath a box reading Ctrl+F8. The rejection is
                        // still worth saying — once, in the status line, where messages are
                        // understood to be about what just happened rather than about the
                        // current state.
                        _hotkeyRegistrationError = null;
                        Status = error;
                    }
                }

                // If there is nothing to roll back to — first launch with the default hotkey
                // already taken — the app deliberately ends up with no hotkey registered at all.
                // The box keeps showing the rejected combination and the error stays under it,
                // which is the honest description of where things stand: nothing is listening,
                // and the Start button is still there.
            }
        }
        finally
        {
            _applyingHotkey = false;
            OnPropertyChanged(nameof(HotkeyError));
            OnPropertyChanged(nameof(HasHotkeyError));
            OnPropertyChanged(nameof(HotkeyWarning));
            OnPropertyChanged(nameof(HasHotkeyWarning));
            StartCommand.NotifyCanExecuteChanged();
        }
    }

    internal static string? ValidateInterval(string? text, out int value)
    {
        value = 0;

        if (!int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return "Enter a whole number of milliseconds.";
        }

        if (parsed < MinIntervalMs)
        {
            return $"Minimum is {MinIntervalMs} ms — faster than that floods the input queue.";
        }

        if (parsed > MaxIntervalMs)
        {
            return $"Maximum is {MaxIntervalMs:N0} ms.";
        }

        value = parsed;
        return null;
    }

    // ------------------------------------------------------------------ commands

    private bool CanStart =>
        !IsRunning && !SendCombo.IsEmpty && !HasIntervalError && CollisionError is null;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        if (ValidateInterval(IntervalText, out var interval) is not null)
        {
            return;
        }

        // Passing the hotkey lets the engine hold off until you have let go of it.
        _engine.Start(SendCombo, interval, HotkeyCombo);
        IsRunning = true;
        Status = RunningStatus();
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private async Task StopAsync()
    {
        await _engine.StopAsync();
        IsRunning = false;
        Status = "Stopped.";
    }

    /// <summary>Start if stopped, stop if running. Invoked from the WM_HOTKEY hook.</summary>
    public async Task ToggleAsync()
    {
        // Defence in depth alongside the engine's release-wait. Windows can deliver more than
        // one WM_HOTKEY for what the user experienced as a single press — auto-repeat
        // suppression is not absolute — and a duplicate arriving here would toggle straight back.
        var now = Environment.TickCount64;
        if (now - _lastToggleTicks < HotkeyDebounceMs)
        {
            return;
        }

        _lastToggleTicks = now;

        if (IsRunning)
        {
            await StopAsync();
        }
        else if (CanStart)
        {
            Start();
        }
    }

    private string RunningStatus()
    {
        var interval = ValidateInterval(IntervalText, out var value) is null ? value : 0;
        return HotkeyCombo.IsEmpty
            ? $"Running — {SendCombo.DisplayName} every {interval} ms."
            : $"Running — {SendCombo.DisplayName} every {interval} ms. {HotkeyCombo.DisplayName} to stop.";
    }

    /// <summary>
    /// Shuts the view model down in the one order that is safe, so no caller has to know it.
    /// </summary>
    /// <remarks>
    /// The hotkey goes first: while it is registered, a WM_HOTKEY can still arrive and start the
    /// engine, and the await below keeps the dispatcher pumping long enough for that to happen.
    /// Stopping the engine second is what guarantees no key is left held down — it waits for the
    /// loop to unwind past its key-up. Saving last means the file reflects the final state.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        _hotKey?.Dispose();
        _hotKey = null;

        await _engine.DisposeAsync().ConfigureAwait(true);

        SaveSettings();
    }
}
