using System.Globalization;
using System.Windows;
using ChronoStroke.Interop;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChronoStroke;

public partial class MainViewModel : ObservableObject
{
    /// <summary>
    /// Guard rail. Below this the machine is flooded with input faster than most windows can
    /// drain it, and recovering means killing the process.
    /// </summary>
    public const int MinIntervalMs = 50;

    public const int MaxIntervalMs = 60_000;

    private readonly RepeatEngine _engine = new();

    private GlobalHotKey? _hotKey;

    /// <summary>Last combination that registered successfully — the target to revert to.</summary>
    private KeyCombo _lastGoodHotkey;

    /// <summary>Guards against re-entering ApplyHotkey when a failure reverts the property.</summary>
    private bool _applyingHotkey;

    private string? _hotkeyRegistrationError;

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

    private static void OnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            action();       // no WPF app running (tests)
            return;
        }

        dispatcher.InvokeAsync(action);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyError), nameof(HasHotkeyError))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial KeyCombo SendCombo { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyError), nameof(HasHotkeyError))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial KeyCombo HotkeyCombo { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IntervalError), nameof(HasIntervalError))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string IntervalText { get; set; } = "250";

    /// <summary>Suppresses saving while settings are being applied from disk.</summary>
    private bool _loading;

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
            IntervalText = Math.Clamp(settings.IntervalMs, MinIntervalMs, MaxIntervalMs)
                .ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _loading = false;
        }
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

        // Keep the last valid interval rather than writing a half-typed one; the text box
        // updates on every keystroke, so "25" exists briefly on the way to "250".
        if (ValidateInterval(IntervalText, out var interval) is not null)
        {
            interval = SettingsStore.Load().IntervalMs;
        }

        var error = SettingsStore.Save(AppSettings.From(SendCombo, HotkeyCombo, interval));
        if (error is not null)
        {
            Status = error;
        }
    }

    partial void OnSendComboChanged(KeyCombo value) => SaveSettings();

    partial void OnIntervalTextChanged(string value) => SaveSettings();

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
                    _hotKey.TryRegister(_lastGoodHotkey, out _);
                }
            }
        }
        finally
        {
            _applyingHotkey = false;
            OnPropertyChanged(nameof(HotkeyError));
            OnPropertyChanged(nameof(HasHotkeyError));
            StartCommand.NotifyCanExecuteChanged();
        }
    }

    public void ReleaseHotkey() => _hotKey?.Dispose();

    private static string? ValidateInterval(string? text, out int value)
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

    public ValueTask DisposeEngineAsync() => _engine.DisposeAsync();
}
