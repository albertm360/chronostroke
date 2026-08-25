using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;
using ChronoStroke;
using ChronoStroke.Interop;

namespace ChronoStroke.Tests;

/// <summary>
/// The register-fail-roll-back-re-register sequence.
/// </summary>
/// <remarks>
/// The subtlest state machine in the app, and until the binder was extracted from the view model
/// it had no coverage at all: registering for real needs a window handle, and takes the key away
/// from the whole machine for as long as the test runs.
/// <para>
/// What matters is that a rejected choice never leaves the app with no working hotkey. The easy
/// mistake is rolling back what the box <em>shows</em> without putting the old registration back,
/// which looks correct on screen while nothing is listening — so these assert against the
/// registration, not just the displayed combination.
/// </para>
/// </remarks>
public class HotkeyBinderTests
{
    private static readonly KeyCombo CtrlF8 = new(0x77, ModifierKeys.Control);
    private static readonly KeyCombo CtrlF9 = new(0x78, ModifierKeys.Control);
    private static readonly KeyCombo CtrlF10 = new(0x79, ModifierKeys.Control);

    /// <summary>Stands in for RegisterHotKey, rejecting whatever it is told to.</summary>
    private sealed class FakeRegistration : IHotKeyRegistration
    {
        /// <summary>Returns an error to refuse a combination, or null to accept it.</summary>
        public Func<KeyCombo, string?> Refuse { get; set; } = _ => null;

        public KeyCombo Current { get; private set; }

        public int Disposals { get; private set; }

        public bool TryRegister(KeyCombo combo, [NotNullWhen(false)] out string? error)
        {
            // The real one drops any existing registration before trying, so a failure leaves
            // nothing registered rather than the previous combination.
            Unregister();

            error = Refuse(combo);
            if (error is not null)
            {
                return false;
            }

            Current = combo;
            return true;
        }

        public void Unregister() => Current = default;

        public void Dispose()
        {
            Disposals++;
            Unregister();
        }
    }

    private static (HotkeyBinder Binder, FakeRegistration Registration) Attached(KeyCombo initial)
    {
        var registration = new FakeRegistration();
        var binder = new HotkeyBinder { Combo = initial };
        binder.Attach(registration);
        return (binder, registration);
    }

    [Fact]
    public void AnAcceptedCombinationIsRegisteredAndShown()
    {
        var (binder, registration) = Attached(CtrlF8);

        Assert.Equal(CtrlF8, binder.Combo);
        Assert.Equal(CtrlF8, registration.Current);
        Assert.True(binder.IsRegistered);
        Assert.Null(binder.Error);
    }

    /// <summary>
    /// The rollback, and the reason these tests exist. Both halves are checked: the box goes back
    /// to the old combination, and the old combination is registered again.
    /// </summary>
    [Fact]
    public void ARejectedCombinationRollsBackAndReregistersTheOldOne()
    {
        var (binder, registration) = Attached(CtrlF8);
        registration.Refuse = combo => combo == CtrlF9 ? "Ctrl+F9 is already in use." : null;

        binder.Combo = CtrlF9;

        Assert.Equal(CtrlF8, binder.Combo);
        Assert.Equal(CtrlF8, registration.Current);
        Assert.True(binder.IsRegistered);
    }

    /// <summary>
    /// A rolled-back rejection is said once, in the status line. Leaving it under the box would
    /// park a permanent-looking complaint about Ctrl+F9 beneath a box reading Ctrl+F8.
    /// </summary>
    [Fact]
    public void ARolledBackRejectionIsReportedButNotLeftUnderTheBox()
    {
        var (binder, registration) = Attached(CtrlF8);
        var reported = new List<string>();
        binder.Rejected += (_, message) => reported.Add(message);
        registration.Refuse = combo => combo == CtrlF9 ? "Ctrl+F9 is already in use." : null;

        binder.Combo = CtrlF9;

        Assert.Equal(["Ctrl+F9 is already in use."], reported);
        Assert.Null(binder.Error);
    }

    /// <summary>
    /// First launch with the saved hotkey already taken. There is nothing to roll back to, so the
    /// app deliberately ends up with nothing registered — and says so under the box, because that
    /// is the honest description of where things stand.
    /// </summary>
    [Fact]
    public void AFirstRejectionWithNothingToFallBackOnKeepsTheErrorVisible()
    {
        var registration = new FakeRegistration
        {
            Refuse = _ => "Ctrl+F8 is already in use by another application.",
        };
        var binder = new HotkeyBinder { Combo = CtrlF8 };
        var reported = new List<string>();
        binder.Rejected += (_, message) => reported.Add(message);

        binder.Attach(registration);

        Assert.Equal(CtrlF8, binder.Combo);
        Assert.False(binder.IsRegistered);
        Assert.Equal("Ctrl+F8 is already in use by another application.", binder.Error);
        Assert.True(binder.HasError);

        // Nothing was rolled back, so there is nothing to report as having just happened — the
        // error under the box is the whole message.
        Assert.Empty(reported);
    }

    /// <summary>
    /// Both the requested combination and the fallback failing. Nothing is listening, and the
    /// error describes the choice the user actually made rather than the one they used to have.
    /// </summary>
    [Fact]
    public void AFailedRollbackLeavesNothingRegisteredAndReportsTheRejection()
    {
        var (binder, registration) = Attached(CtrlF8);
        registration.Refuse = _ => "everything is taken";

        binder.Combo = CtrlF9;

        Assert.Equal(CtrlF9, binder.Combo);
        Assert.False(binder.IsRegistered);
        Assert.Equal("everything is taken", binder.Error);
    }

    /// <summary>
    /// A rejection after a successful change falls back to the most recent success, not to the
    /// first one.
    /// </summary>
    [Fact]
    public void TheFallbackIsTheMostRecentSuccess()
    {
        var (binder, registration) = Attached(CtrlF8);
        binder.Combo = CtrlF9;
        Assert.Equal(CtrlF9, registration.Current);

        registration.Refuse = combo => combo == CtrlF10 ? "taken" : null;
        binder.Combo = CtrlF10;

        Assert.Equal(CtrlF9, binder.Combo);
        Assert.Equal(CtrlF9, registration.Current);
    }

    /// <summary>
    /// Settings load before the window has a handle, so the binder has to hold a combination it
    /// cannot register yet and apply it when the handle arrives.
    /// </summary>
    [Fact]
    public void ACombinationSetBeforeAttachingIsAppliedOnAttach()
    {
        var binder = new HotkeyBinder();
        var registration = new FakeRegistration();

        binder.Combo = CtrlF8;
        Assert.Equal(CtrlF8, binder.Combo);
        Assert.False(binder.IsRegistered);

        binder.Attach(registration);

        Assert.Equal(CtrlF8, registration.Current);
        Assert.True(binder.IsRegistered);
    }

    [Fact]
    public void ChangedFiresForAcceptedAndRejectedAlike()
    {
        var (binder, registration) = Attached(CtrlF8);
        var count = 0;
        binder.Changed += (_, _) => count++;

        binder.Combo = CtrlF9;                                  // accepted
        registration.Refuse = combo => combo == CtrlF10 ? "taken" : null;
        binder.Combo = CtrlF10;                                 // rejected, rolled back

        Assert.Equal(2, count);
    }

    /// <summary>
    /// Disposing drops the registration. On shutdown this runs before the engine is stopped: while
    /// it is live a WM_HOTKEY can still arrive and start the loop again on the way out.
    /// </summary>
    [Fact]
    public void DisposingDropsTheRegistration()
    {
        var (binder, registration) = Attached(CtrlF8);

        binder.Dispose();

        Assert.Equal(1, registration.Disposals);
        Assert.True(registration.Current.IsEmpty);
        Assert.False(binder.IsRegistered);
    }

    [Fact]
    public void DisposingTwiceIsHarmless()
    {
        var (binder, _) = Attached(CtrlF8);

        binder.Dispose();
        binder.Dispose();

        Assert.False(binder.IsRegistered);
    }

    /// <summary>
    /// Setting the same combination again re-registers rather than short-circuiting. Attach relies
    /// on it, and so does recovering when something else has taken the key in the meantime.
    /// </summary>
    [Fact]
    public void SettingTheSameCombinationAgainReregistersIt()
    {
        var (binder, registration) = Attached(CtrlF8);
        registration.Unregister();
        Assert.False(binder.IsRegistered);

        binder.Combo = CtrlF8;

        Assert.True(binder.IsRegistered);
        Assert.Equal(CtrlF8, registration.Current);
    }
}
