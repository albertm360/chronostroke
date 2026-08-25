using ChronoStroke.Interop;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChronoStroke;

/// <summary>
/// The start/stop hotkey: which combination the box shows, whether it is registered, and what to
/// say when it is not.
/// </summary>
/// <remarks>
/// A rejected choice never leaves the app with no working hotkey. Registration is attempted, and
/// on failure the last combination that worked is put back — both in the box and with the system
/// — so the only way to end up with nothing listening is for the very first attempt to fail, on a
/// launch where the saved hotkey is already held by something else.
/// <para>
/// This used to live in the view model behind a re-entrancy flag, because rolling back meant
/// assigning the view model's own bound property, which re-entered the same method. Owning the
/// combination removes the cause rather than guarding it: the rollback sets the backing field and
/// raises the change notification, so there is nothing to re-enter and no flag to get wrong.
/// </para>
/// </remarks>
internal sealed partial class HotkeyBinder : ObservableObject, IDisposable
{
    private IHotKeyRegistration? _registration;
    private KeyCombo _combo;

    /// <summary>The last combination that registered — what a rejection reverts to.</summary>
    private KeyCombo _lastGood;

    /// <summary>
    /// The combination the box shows. Setting it attempts the registration; if that fails and
    /// there is something to fall back to, what comes back out is the fallback rather than what
    /// went in.
    /// </summary>
    public KeyCombo Combo
    {
        get => _combo;
        set => Apply(value);
    }

    /// <summary>
    /// Null when the hotkey is registered, otherwise why it is not. A rejection that was rolled
    /// back does not appear here — the box has gone back to showing the old combination, so a
    /// complaint underneath it would describe a state that is no longer on screen. Those are
    /// reported through <see cref="Rejected"/> instead.
    /// </summary>
    public string? Error { get; private set; }

    /// <summary>Whether anything is actually listening for the hotkey right now.</summary>
    public bool IsRegistered => _registration?.Current.IsEmpty == false;

    /// <summary>Raised after the combination or the error changes.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised when a rejected combination was rolled back, carrying the reason. Said once, in the
    /// status line, where messages are understood to be about what just happened rather than
    /// about the current state.
    /// </summary>
    public event EventHandler<string>? Rejected;

    /// <summary>
    /// Hands over the registration to use and applies whatever combination is already set.
    /// Separate from construction because RegisterHotKey needs a real window handle, which does
    /// not exist until the window is sourced.
    /// </summary>
    public void Attach(IHotKeyRegistration registration)
    {
        _registration = registration;
        Apply(_combo);
    }

    private void Apply(KeyCombo requested)
    {
        // Before the window exists there is nothing to register against, so the combination is
        // simply remembered — Attach applies it once there is.
        if (_registration is null)
        {
            SetCombo(requested);
            return;
        }

        if (_registration.TryRegister(requested, out var error))
        {
            _lastGood = requested;
            Error = null;
            SetCombo(requested);
            return;
        }

        // Roll back to whatever last worked and put that registration back in place.
        if (!_lastGood.IsEmpty && _lastGood != requested)
        {
            if (_registration.TryRegister(_lastGood, out _))
            {
                Error = null;
                SetCombo(_lastGood);
                Rejected?.Invoke(this, error);
                return;
            }
        }

        // Nothing to fall back to — a first launch where the saved hotkey is already taken. The
        // box keeps showing the rejected combination and the error stays under it, which is the
        // honest description of where things stand: nothing is listening. Start disables itself
        // in that state; see MainViewModel.CanStart for why it has to.
        Error = error;
        SetCombo(requested);
    }

    private void SetCombo(KeyCombo value)
    {
        var comboChanged = _combo != value;
        _combo = value;

        if (comboChanged)
        {
            OnPropertyChanged(nameof(Combo));
        }

        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsRegistered));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool HasError => Error is not null;

    /// <summary>
    /// Drops the registration. Called before the engine is stopped on shutdown: while it is live
    /// a WM_HOTKEY can still arrive and start the loop again on the way out.
    /// </summary>
    public void Dispose()
    {
        _registration?.Dispose();
        _registration = null;
    }
}
