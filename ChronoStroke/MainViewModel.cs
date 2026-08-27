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

    /// <summary>Smallest useful nudge from the interval's arrows.</summary>
    public const int MinStepMs = 1;

    /// <summary>
    /// Largest nudge. A step bigger than this crosses most of the interval's own range in one
    /// click, at which point typing the number is quicker than pressing an arrow.
    /// </summary>
    public const int MaxStepMs = 1_000;

    private readonly RepeatEngine _engine = new();

    /// <summary>
    /// The dispatcher of the thread that built this view model — the UI thread. Captured rather
    /// than reached for through Application.Current so the view model does not depend on a
    /// running WPF application to marshal its own events.
    /// </summary>
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    /// <summary>What is currently on disk, so an unchanged configuration is not rewritten.</summary>
    private AppSettings? _lastSaved;

    /// <summary>Suppresses saving while settings are being applied from disk.</summary>
    private bool _loading;

    /// <summary>How long the settings file waits for editing to stop before it is rewritten.</summary>
    private const int SaveDebounceMs = 500;

    /// <summary>Restarted by every edit; writes the file when it finally gets to run.</summary>
    private readonly DispatcherTimer _saveTimer;

    /// <summary>Shortest gap between two accepted hotkey toggles.</summary>
    private const int HotkeyDebounceMs = 250;

    private long _lastToggleTicks;

    /// <summary>
    /// Set for as long as a Stop is in progress, so the run being torn down cannot write over
    /// the status line on its way out. See <see cref="SetRunStatus"/>.
    /// </summary>
    private bool _stopping;

    /// <summary>The interval box, its bounds and the last usable value it held.</summary>
    public BoundedIntField Interval { get; } = CreateIntervalField();

    /// <summary>How far each press of an interval arrow moves it.</summary>
    public BoundedIntField Step { get; } = CreateStepField();

    /// <summary>The start/stop hotkey, and whether anything is listening for it.</summary>
    public HotkeyBinder Hotkey { get; } = new();

    /// <summary>
    /// The two boxes, configured. Static so the tests can exercise the real bounds and the real
    /// messages without constructing a view model — whose constructor loads settings from the
    /// user's own %AppData%.
    /// </summary>
    internal static BoundedIntField CreateIntervalField() => new(
        MinIntervalMs,
        MaxIntervalMs,
        AppSettings.Default.IntervalMs,
        $"Minimum is {MinIntervalMs} ms — faster than that floods the input queue.",
        $"Maximum is {MaxIntervalMs:N0} ms.");

    /// <inheritdoc cref="CreateIntervalField"/>
    internal static BoundedIntField CreateStepField()
    {
        // One message for both ends: the step has no reason to give beyond the range itself,
        // where the interval's floor has the input queue to explain.
        var outOfRange = $"Step must be between {MinStepMs} and {MaxStepMs:N0} ms.";
        return new BoundedIntField(
            MinStepMs, MaxStepMs, AppSettings.Default.IntervalStepMs, outOfRange, outOfRange);
    }

    public MainViewModel()
    {
        // This constructor overload starts the timer, which is not what an idle app wants — it
        // is armed by the first edit, not by existing. Background priority because a settings
        // write must never come before rendering or input.
        _saveTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(SaveDebounceMs),
            DispatcherPriority.Background,
            (_, _) => FlushSettings(),
            _dispatcher);
        _saveTimer.Stop();

        // Typing in either box saves, and moves the guard rails the spinner arrows disable at.
        Interval.Changed += (_, _) =>
        {
            SaveSettings();
            NotifyIntervalDependents();
        };
        Step.Changed += (_, _) =>
        {
            SaveSettings();
            StepUpCommand.NotifyCanExecuteChanged();
            StepDownCommand.NotifyCanExecuteChanged();
        };

        // The hotkey feeds three things the view model owns: the error shown under the box, which
        // combines with the collision check; the same-key warning; and whether Start is allowed
        // to run at all, which needs a registration to exist.
        Hotkey.Changed += (_, _) =>
        {
            SaveSettings();
            NotifyHotkeyDependents();
        };

        // A rejection that was rolled back. The box has already gone back to showing the old
        // combination, so this belongs in the status line rather than under the box.
        Hotkey.Rejected += (_, message) => Status = message;

        // The loop runs on the thread pool, so these arrive off the UI thread.
        _engine.SendFailed += (_, message) => OnUiThread(() => SetRunStatus(message));
        _engine.WaitingForReleaseChanged += (_, waiting) => OnUiThread(() =>
            SetRunStatus(waiting
                ? $"Waiting for you to let go of {Hotkey.Combo.DisplayName}…"
                : RunningStatus()));

        // The loop can end without anyone asking it to. SendFailed has already put the reason in
        // the status line by the time this arrives; this is what unlocks the fields and turns the
        // dot grey to match it, instead of leaving the two contradicting each other.
        _engine.Stopped += (_, _) => OnUiThread(NotifyRunStateChanged);

        ApplySettings(SettingsStore.Load());
    }

    /// <summary>
    /// What the interval's contents govern besides itself: Start refuses an unusable interval,
    /// and the arrows grey out once it reaches a guard rail. The attributes that used to do this
    /// only work on properties of this class, so moving the box out moved this here too.
    /// </summary>
    private void NotifyIntervalDependents()
    {
        StartCommand.NotifyCanExecuteChanged();
        StepUpCommand.NotifyCanExecuteChanged();
        StepDownCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Takes a status line from the engine, unless a Stop has already begun.
    /// </summary>
    /// <remarks>
    /// The loop reports on a thread-pool thread and the report is marshalled here, so it can be
    /// queued while the run that produced it is being torn down. Anything it has to say about
    /// that run is stale by then, and writing it would leave the status line describing a loop
    /// that is no longer running — over the top of "Stopped.", which is the one thing the user
    /// just asked for and the only line still true.
    /// <para>
    /// Not reachable as the code stands, and the flag is here to stop that being an accident.
    /// Every engine event goes through <see cref="OnUiThread"/> at the dispatcher's default
    /// priority, and StopAsync writes "Stopped." only after awaiting the loop past its own
    /// finally — so everything the run queued is already ahead of it, and same-priority
    /// dispatcher work runs in order. That argument is correct and entirely invisible: it lives
    /// in the interaction between three methods and would be broken by changing a priority, or
    /// by a Stop that stopped joining the loop. A flag set before the await says it locally.
    /// </para>
    /// <para>
    /// A run generation on the events themselves, as the review proposed, would also cover
    /// events crossing from one run into the next. That cannot happen here: Start refuses while
    /// a run is live, and StopAsync joins the loop before returning, so there is never more than
    /// one run in flight. It would be a wider mechanism for a case the engine already prevents.
    /// </para>
    /// </remarks>
    private void SetRunStatus(string message)
    {
        if (_stopping)
        {
            return;
        }

        Status = message;
    }

    /// <summary>
    /// What the run state governs. This is the set of notifications the attributes on IsRunning
    /// used to generate; they only work on a property this class stores, and it no longer does.
    /// </summary>
    private void NotifyRunStateChanged()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(CanEdit));
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        StepUpCommand.NotifyCanExecuteChanged();
        StepDownCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// What the hotkey governs beyond itself. The error and the warning are both this view
    /// model's, because each combines the hotkey with the key being sent — something neither the
    /// binder nor the capture box can see on its own.
    /// </summary>
    private void NotifyHotkeyDependents()
    {
        OnPropertyChanged(nameof(HotkeyError));
        OnPropertyChanged(nameof(HotkeyWarning));
        StartCommand.NotifyCanExecuteChanged();
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
    [NotifyPropertyChangedFor(nameof(HotkeyError), nameof(HotkeyWarning))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial KeyCombo SendCombo { get; set; }

    /// <summary>
    /// Whether the loop is running. Read from the engine rather than stored, because the engine
    /// is the only thing that knows — including when the loop ends without being asked to.
    /// </summary>
    public bool IsRunning => _engine.IsRunning;

    [ObservableProperty]
    public partial string Status { get; set; } = "Pick a key to send.";

    /// <summary>Description of the guard rails, shown under the boxes and taken from them.</summary>
    public string RangeHint =>
        $"{Interval.Min}–{Interval.Max:N0} ms, in steps of {Step.Min}–{Step.Max:N0} ms";

    /// <summary>Settings are locked while the loop runs — the engine captured them at Start.</summary>
    public bool CanEdit => !IsRunning;

    /// <summary>
    /// The sent key and the start/stop hotkey must differ. If they were the same, every injected
    /// keystroke would match our own hotkey registration and toggle the loop back off — the app
    /// would fight itself. Blocking is kinder than letting it misbehave inexplicably.
    /// </summary>
    private string? CollisionError =>
        !SendCombo.IsEmpty && SendCombo == Hotkey.Combo
            ? "The hotkey must differ from the key being sent, or the repeated keystroke will trigger it."
            : null;

    /// <summary>Null when there is nothing wrong — which is also what collapses the banner.</summary>
    public string? HotkeyError => CollisionError ?? Hotkey.Error;

    /// <summary>
    /// Advisory, not blocking. The two combinations differ, so nothing is wrong yet — but they
    /// share a virtual key, and the modifiers are the only thing keeping them apart. Send F8
    /// with Ctrl+F8 as the hotkey and the configuration works exactly as intended until the
    /// moment the user happens to hold Ctrl for something unrelated, at which point the loop's
    /// own F8 becomes Ctrl+F8 and switches itself off. WaitForTriggerReleaseAsync covers the
    /// start; nothing can cover the middle, so the honest answer is to say so up front.
    /// </summary>
    public string? HotkeyWarning =>
        CollisionError is null && !SendCombo.IsEmpty && !Hotkey.Combo.IsEmpty
        && SendCombo.VirtualKey == Hotkey.Combo.VirtualKey
            ? $"{Hotkey.Combo.DisplayName} and {SendCombo.DisplayName} are the same key with "
              + "different modifiers. Holding a modifier while the loop runs can turn the "
              + "repeated keystroke into the hotkey and stop it."
            : null;

    // ---------------------------------------------------------------- settings

    private void ApplySettings(AppSettings settings)
    {
        _loading = true;
        try
        {
            // Sanitised for the same reason the interval below is clamped: the file is
            // hand-editable, so nothing read from it is trusted as it stands.
            SendCombo = settings.SendCombo.Sanitised();
            Hotkey.Combo = settings.HotkeyCombo.Sanitised();

            // Clamp rather than trust. The file is plain text in a folder the user can open, so
            // a hand-edited 1 ms interval must not slip past the guard rail the UI enforces.
            Interval.SetClamped(settings.IntervalMs);

            // Zero means the file was written before the step existed, not that someone asked
            // for a zero step — clamping that to 1 ms would silently give every upgrader the
            // slowest possible arrows. Anything else goes through the same guard rails.
            Step.SetClamped(settings.IntervalStepMs == 0
                ? AppSettings.Default.IntervalStepMs
                : settings.IntervalStepMs);
        }
        finally
        {
            _loading = false;
        }

        // What is now on screen, so the first edit is compared against it rather than written
        // back unchanged.
        _lastSaved = AppSettings.From(SendCombo, Hotkey.Combo, Interval.LastValid, Step.LastValid);
    }

    /// <summary>
    /// Notes that the configuration has changed and starts the clock on writing it out.
    /// </summary>
    /// <remarks>
    /// Every accepted edit lands here, and there are far more of them than there are things worth
    /// writing. The boxes update their bindings on each keystroke, so typing 100 into the step box
    /// arrives as 1, then 10, then 100 — three values, all of them valid, all of them previously
    /// a full write. Holding a spinner arrow was worse: the repeat interval is 80 ms, so the file
    /// was rewritten about twelve times a second, each time creating the directory, writing a
    /// temp file and renaming it, synchronously on the UI thread and through whatever real-time
    /// scanner is installed.
    /// <para>
    /// Restarting the timer on each edit collapses all of that into one write once the typing
    /// stops. The cost is a window — up to the debounce — in which the newest edit is not yet on
    /// disk if the process is killed outright. That is the same class of loss the README already
    /// documents for End Task, every ordinary exit flushes through
    /// <see cref="DisposeAsync"/>, and the write itself is still atomic, so the file is never
    /// found half-written.
    /// </para>
    /// </remarks>
    public void SaveSettings()
    {
        if (_loading)
        {
            return;
        }

        // Restart rather than let it run: the clock measures quiet, not elapsed time.
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>
    /// Writes the configuration out now, if it differs from what is already on disk.
    /// </summary>
    private void FlushSettings()
    {
        _saveTimer.Stop();

        if (_loading)
        {
            return;
        }

        var settings = AppSettings.From(SendCombo, Hotkey.Combo, Interval.LastValid, Step.LastValid);

        // Comparing against what was last written skips the disk entirely when the debounce has
        // collapsed a run of edits that ended where it started — retyping the same number, or
        // nudging an arrow up and back down.
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


    // ------------------------------------------------------------------ hotkey

    /// <summary>
    /// Called once the window has an HWND. RegisterHotKey needs a real handle, which does
    /// not exist until the window is sourced.
    /// </summary>
    public void AttachWindow(IntPtr windowHandle) => Hotkey.Attach(new GlobalHotKey(windowHandle));

    // ------------------------------------------------------------------ commands

    private bool CanStepUp => CanEdit && Interval.ValueOrLastValid < Interval.Max;

    private bool CanStepDown => CanEdit && Interval.ValueOrLastValid > Interval.Min;

    // Nudging sets the interval's text, which is the whole update: validation, the last-good
    // value and the save all hang off that, exactly as they do when the number is typed.
    [RelayCommand(CanExecute = nameof(CanStepUp))]
    private void StepUp() => Interval.Nudge(Step.ValueOrLastValid);

    [RelayCommand(CanExecute = nameof(CanStepDown))]
    private void StepDown() => Interval.Nudge(-Step.ValueOrLastValid);

    /// <summary>
    /// The last term is a safety interlock rather than a validation rule. With no hotkey
    /// registered there is no keyboard way to stop the loop, and the only remaining one is
    /// reaching this window with the mouse while the app injects a key of the user's choosing
    /// into whatever has focus, every 50 ms — with Alt+Tab, Enter and Alt+F4 all being things
    /// they may have picked as the key to send. Refusing to start is the difference between an
    /// inconvenience and needing the power button.
    /// </summary>
    private bool CanStart =>
        !IsRunning && !SendCombo.IsEmpty && !Interval.HasError && CollisionError is null
        && Hotkey.IsRegistered;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        if (Interval.Error is not null)
        {
            return;
        }

        _stopping = false;

        // Passing the hotkey lets the engine hold off until you have let go of it.
        _engine.Start(SendCombo, Interval.ValueOrLastValid, Hotkey.Combo);
        NotifyRunStateChanged();
        Status = RunningStatus();
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private async Task StopAsync()
    {
        // Set before the await, not after: the point is to disown whatever the loop has already
        // queued about the run being torn down, and that runs while this awaits.
        _stopping = true;

        await _engine.StopAsync();
        NotifyRunStateChanged();
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
        var interval = Interval.ValueOrLastValid;
        return Hotkey.Combo.IsEmpty
            ? $"Running — {SendCombo.DisplayName} every {interval} ms."
            : $"Running — {SendCombo.DisplayName} every {interval} ms. {Hotkey.Combo.DisplayName} to stop.";
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
        Hotkey.Dispose();

        await _engine.DisposeAsync().ConfigureAwait(true);

        // Flush rather than queue. The dispatcher stops pumping moments after this returns, so a
        // debounced write would never get its tick and the last edit before closing would be the
        // one edit that did not persist.
        FlushSettings();
    }
}
