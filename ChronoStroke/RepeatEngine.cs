using ChronoStroke.Interop;

namespace ChronoStroke;

/// <summary>
/// Repeatedly sends one key combination until stopped.
/// </summary>
/// <remarks>
/// Start and Stop are expected to be called from the UI thread only (a button, or the WM_HOTKEY
/// hook, which is delivered on the UI thread). The loop itself runs on the thread pool.
/// </remarks>
public sealed class RepeatEngine : IAsyncDisposable
{
    /// <summary>Target key-down duration. Trimmed if the interval is too short to fit it.</summary>
    private const int HoldMilliseconds = 40;

    private const int ReleasePollMilliseconds = 15;

    /// <summary>How long the wait must run before it is worth telling the user about.</summary>
    private const int ReleaseNoticeAfterMilliseconds = 400;

    /// <summary>Keys must read as up for this long before we trust it and start.</summary>
    private const int ReleaseSettleMilliseconds = 40;

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public bool IsRunning => _cts is not null;

    /// <summary>Raised when SendInput reports a failure. Fires on a thread-pool thread.</summary>
    public event EventHandler<string>? SendFailed;

    /// <summary>
    /// True when the loop is holding off because trigger keys are still down, false once it
    /// starts sending. Fires on a thread-pool thread.
    /// </summary>
    public event EventHandler<bool>? WaitingForReleaseChanged;

    /// <param name="triggerCombo">
    /// The hotkey that started this, if any. Injection is held off until its keys are physically
    /// released — see <see cref="WaitForTriggerReleaseAsync"/> for why that matters.
    /// </param>
    public void Start(KeyCombo combo, int intervalMs, KeyCombo triggerCombo = default)
    {
        if (IsRunning || combo.IsEmpty)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(combo, intervalMs, triggerCombo, _cts.Token));
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        var loop = _loop;
        _cts = null;
        _loop = null;

        if (cts is null)
        {
            return;
        }

        await cts.CancelAsync();

        if (loop is not null)
        {
            // The loop swallows its own cancellation, so this is just waiting for it to unwind
            // — importantly, past the finally block that releases the key.
            await loop.ConfigureAwait(false);
        }

        cts.Dispose();
    }

    private async Task RunAsync(KeyCombo combo, int intervalMs, KeyCombo triggerCombo, CancellationToken ct)
    {
        // Never let the hold swallow the whole interval; at the 50 ms floor this gives 25 ms
        // down, 25 ms up rather than a key that is held permanently.
        var hold = Math.Min(HoldMilliseconds, intervalMs / 2);
        string? lastReportedFailure = null;

        try
        {
            await WaitForTriggerReleaseAsync(triggerCombo, ct).ConfigureAwait(false);

            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs));
            while (true)
            {
                var down = KeystrokeSender.Press(combo);
                await Task.Delay(hold, ct).ConfigureAwait(false);
                var up = KeystrokeSender.Release(combo);

                // Report a failure once rather than once per tick — a broken send fails every
                // time, and 20 identical messages a second is noise, not information.
                var failure = !down.Success ? down.Describe() : !up.Success ? up.Describe() : null;
                if (failure is not null && failure != lastReportedFailure)
                {
                    lastReportedFailure = failure;
                    SendFailed?.Invoke(this, failure);
                }
                else if (failure is null)
                {
                    lastReportedFailure = null;
                }

                if (!await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on Stop.
        }
        finally
        {
            // Unconditional. If cancellation landed between Press and Release the key is still
            // physically down as far as the target window is concerned, and leaving it that way
            // means a game holding an action forever. A key-up for a key that was never down is
            // harmless, so this is safe to send even on the paths that did not press anything.
            KeystrokeSender.Release(combo);
        }
    }

    /// <summary>
    /// Blocks the first keystroke until the keys that triggered the start are physically up.
    /// </summary>
    /// <remarks>
    /// Two distinct problems, one cause — you are still holding the hotkey when the loop begins.
    /// <list type="number">
    /// <item>Every injected keystroke picks up your held modifiers. Start with Ctrl+F8 while
    /// sending X and the target window receives Ctrl+X, which is Cut in most apps and whatever
    /// Ctrl+X happens to be bound to in a game.</item>
    /// <item>Worse, it re-triggers the hotkey. MOD_NOREPEAT suppresses plain auto-repeat, but an
    /// unrelated keystroke arriving in between resets that suppression, so the next auto-repeat
    /// of the held hotkey fires a fresh WM_HOTKEY. The loop's own output toggles the loop off,
    /// then on, then off — measured at 1 message with nothing in between versus 6 with an
    /// injected key between each repeat.</item>
    /// </list>
    /// Waiting for release removes the cause of both: while you are still holding the keys we
    /// inject nothing, so there is nothing to reset the suppression or to pick up your modifiers.
    /// </remarks>
    private async Task WaitForTriggerReleaseAsync(KeyCombo triggerCombo, CancellationToken ct)
    {
        if (!AnyTriggerKeyDown(triggerCombo))
        {
            return;
        }

        var start = Environment.TickCount64;
        var announced = false;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (!AnyTriggerKeyDown(triggerCombo))
            {
                // Confirm it stayed up rather than acting on one sample between key repeats.
                await Task.Delay(ReleaseSettleMilliseconds, ct).ConfigureAwait(false);
                if (!AnyTriggerKeyDown(triggerCombo))
                {
                    break;
                }
            }

            // No deadline here, deliberately. An earlier version gave up after three seconds and
            // started sending anyway, on the theory that a wedged key should not hang the app.
            // That reintroduced the very bug this method exists to prevent: hold the hotkey for
            // longer than the ceiling and the loop begins injecting into your held modifiers,
            // which re-triggers the hotkey and switches itself off. Since a timeout cannot tell
            // a wedged key from a user who is simply still holding one, waiting is the only
            // answer that is right in both cases — sending nothing is always recoverable,
            // sending the wrong thing into a game is not. Stop still works throughout.
            if (!announced && Environment.TickCount64 - start > ReleaseNoticeAfterMilliseconds)
            {
                announced = true;
                WaitingForReleaseChanged?.Invoke(this, true);
            }

            await Task.Delay(ReleasePollMilliseconds, ct).ConfigureAwait(false);
        }

        if (announced)
        {
            WaitingForReleaseChanged?.Invoke(this, false);
        }
    }

    private static bool AnyTriggerKeyDown(KeyCombo triggerCombo)
    {
        // Any modifier at all, not just the ones in the trigger: whatever you are holding gets
        // combined into the injected keystroke, regardless of how the loop was started.
        if (IsDown(NativeMethods.VK_CONTROL) || IsDown(NativeMethods.VK_SHIFT)
            || IsDown(NativeMethods.VK_MENU) || IsDown(NativeMethods.VK_LWIN)
            || IsDown(NativeMethods.VK_RWIN))
        {
            return true;
        }

        return !triggerCombo.IsEmpty && IsDown(triggerCombo.VirtualKey);
    }

    private static bool IsDown(ushort vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
