using System.Collections.Concurrent;
using System.Windows.Input;
using ChronoStroke;
using ChronoStroke.Interop;

namespace ChronoStroke.Tests;

/// <summary>
/// The repeat loop's lifecycle, with the batches recorded instead of injected.
/// </summary>
/// <remarks>
/// The property worth guarding is the key-up in RunAsync's finally: if cancellation lands between
/// the press and the release, the target window still believes the key is held, and for a game
/// that means an action stuck on until the user taps the key themselves. Before the engine had a
/// send seam this could only be checked by injecting real keystrokes into the desktop of whoever
/// ran the tests, so it was never checked at all.
/// <para>
/// The loop starts by waiting for physically-held modifiers to come up. These tests pass an empty
/// trigger combo, so that check reduces to "is any modifier down right now" — false on a build
/// agent, and false on a developer machine unless someone is leaning on Ctrl.
/// </para>
/// </remarks>
public class RepeatEngineTests
{
    private static readonly KeyCombo X = new(0x58, ModifierKeys.None);

    private static bool IsKeyUp(NativeMethods.INPUT[] batch) =>
        batch.Length > 0
        && (batch[^1].U.ki.dwFlags & NativeMethods.KEYEVENTF_KEYUP) != 0;

    /// <summary>How long a test will wait for the loop to get going before giving up.</summary>
    private const int WaitTimeoutMs = 10_000;

    /// <summary>Records every batch the loop sends and reports success, as SendInput would.</summary>
    private sealed class Recorder
    {
        private readonly ConcurrentQueue<NativeMethods.INPUT[]> _batches = new();

        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public NativeMethods.INPUT[][] Batches => [.. _batches];

        /// <summary>Completes the moment the loop sends anything at all.</summary>
        public Task Started => _started.Task;

        public SendOutcome Send(NativeMethods.INPUT[] batch)
        {
            _batches.Enqueue(batch);
            _started.TrySetResult();
            return new SendOutcome(batch.Length, (uint)batch.Length, 0);
        }
    }

    /// <summary>
    /// Waits until the loop is genuinely under way, rather than assuming a fixed delay was enough.
    /// </summary>
    /// <remarks>
    /// CI caught this out where a developer machine never does. A test that starts the loop, waits
    /// 15 ms and then stops it is assuming the thread pool dequeued the work item inside those
    /// 15 ms — and on a loaded runner it does not. Start hands the token to Task.Run, so a Stop
    /// arriving first makes the pool skip RunAsync altogether: nothing is sent, and an assertion
    /// on the last batch reads off the end of an empty array.
    /// <para>
    /// An empty recorder is proof of exactly that, which is what makes this the right signal to
    /// wait on: RunAsync's finally sends a key-up on every path it can take, so any entry at all
    /// leaves at least one batch behind.
    /// </para>
    /// </remarks>
    private static async Task WaitForBatchesAsync(Recorder recorder, int count)
    {
        var deadline = Environment.TickCount64 + WaitTimeoutMs;

        while (recorder.Batches.Length < count)
        {
            Assert.True(
                Environment.TickCount64 < deadline,
                $"expected at least {count} batches within {WaitTimeoutMs} ms, "
                + $"saw {recorder.Batches.Length}");

            await Task.Delay(5);
        }
    }

    /// <summary>
    /// The guarantee. Stop lands inside the 40 ms hold, so the loop never reaches its own
    /// release — the finally is the only thing that can put the key back up.
    /// </summary>
    [Fact]
    public async Task StoppingMidHoldStillReleasesTheKey()
    {
        var recorder = new Recorder();
        await using var engine = new RepeatEngine { Send = recorder.Send };

        // Interval 1000 gives the full 40 ms hold. Stopping the instant the press is recorded
        // lands inside it — and unlike a fixed delay, it cannot land before the loop has begun.
        engine.Start(X, intervalMs: 1000);
        await recorder.Started.WaitAsync(TimeSpan.FromMilliseconds(WaitTimeoutMs));
        await engine.StopAsync();

        var batches = recorder.Batches;
        Assert.NotEmpty(batches);
        Assert.False(IsKeyUp(batches[0]), "the loop should press before it releases");
        Assert.True(IsKeyUp(batches[^1]), "the key must be up after the loop unwinds");
    }

    /// <summary>
    /// The same guarantee on the ordinary path: however many cycles ran, the last thing sent is
    /// always a release.
    /// </summary>
    [Fact]
    public async Task TheLastBatchIsAlwaysAReleaseAfterSeveralCycles()
    {
        var recorder = new Recorder();
        await using var engine = new RepeatEngine { Send = recorder.Send };

        engine.Start(X, intervalMs: 50);
        await WaitForBatchesAsync(recorder, 4);
        await engine.StopAsync();

        var batches = recorder.Batches;
        Assert.True(IsKeyUp(batches[^1]), "the key must be up after the loop unwinds");
    }

    [Fact]
    public async Task PressesAndReleasesAlternateWhileTheLoopRuns()
    {
        var recorder = new Recorder();
        await using var engine = new RepeatEngine { Send = recorder.Send };

        engine.Start(X, intervalMs: 50);
        await WaitForBatchesAsync(recorder, 4);
        await engine.StopAsync();

        // The finally adds one extra release on the way out, so the pairs are checked rather
        // than the whole sequence.
        var batches = recorder.Batches;
        for (var i = 0; i + 1 < batches.Length - 1; i += 2)
        {
            Assert.False(IsKeyUp(batches[i]), $"batch {i} should be a press");
            Assert.True(IsKeyUp(batches[i + 1]), $"batch {i + 1} should be a release");
        }
    }

    /// <summary>An empty combination has nothing to send, so the loop never starts.</summary>
    [Fact]
    public async Task AnEmptyComboSendsNothing()
    {
        var recorder = new Recorder();
        await using var engine = new RepeatEngine { Send = recorder.Send };

        engine.Start(default, intervalMs: 50);
        await Task.Delay(60);

        Assert.False(engine.IsRunning);
        Assert.Empty(recorder.Batches);
    }

    /// <summary>
    /// A broken send fails every tick. Reporting it once rather than twenty times a second is
    /// what keeps the status line information rather than noise.
    /// </summary>
    [Fact]
    public async Task ARepeatedFailureIsReportedOnce()
    {
        var reports = new ConcurrentQueue<string>();
        var sends = 0;

        await using var engine = new RepeatEngine
        {
            // Nothing inserted, with a plausible Win32 error — what a blocked SendInput looks like.
            Send = batch =>
            {
                Interlocked.Increment(ref sends);
                return new SendOutcome(batch.Length, 0, 5);
            },
        };
        engine.SendFailed += (_, message) => reports.Enqueue(message);

        engine.Start(X, intervalMs: 50);

        // Counted rather than timed. A fixed delay on a slow runner can pass this test without
        // ever reaching a second failing tick, which is the only thing that could produce a
        // second report — it would be asserting that nothing happened yet, not that the
        // de-duplication works. Four cycles is eight sends.
        var deadline = Environment.TickCount64 + WaitTimeoutMs;
        while (Volatile.Read(ref sends) < 8)
        {
            Assert.True(
                Environment.TickCount64 < deadline,
                $"expected 8 sends within {WaitTimeoutMs} ms, saw {Volatile.Read(ref sends)}");

            await Task.Delay(5);
        }

        await engine.StopAsync();

        Assert.Single(reports);
    }

    [Fact]
    public async Task StoppingWhenNothingIsRunningIsHarmless()
    {
        await using var engine = new RepeatEngine { Send = _ => new SendOutcome(0, 0, 0) };

        await engine.StopAsync();
        await engine.StopAsync();

        Assert.False(engine.IsRunning);
    }

    /// <summary>
    /// Start while already running is ignored rather than starting a second loop — two loops on
    /// the same key would interleave presses and releases and leave the key state ambiguous.
    /// </summary>
    [Fact]
    public async Task StartingTwiceDoesNotStartASecondLoop()
    {
        var recorder = new Recorder();
        await using var engine = new RepeatEngine { Send = recorder.Send };

        engine.Start(X, intervalMs: 1000);
        engine.Start(X, intervalMs: 1000);
        await recorder.Started.WaitAsync(TimeSpan.FromMilliseconds(WaitTimeoutMs));
        await engine.StopAsync();

        // One press from the single loop, one release from its finally. A second loop would have
        // pressed again.
        Assert.Equal(2, recorder.Batches.Length);
    }

    /// <summary>
    /// A sender that throws on the press and works on the release — enough to kill the loop
    /// through RunAsync's catch-all while leaving the key-up in its finally able to succeed.
    /// </summary>
    private static Func<NativeMethods.INPUT[], SendOutcome> BreaksOnPress(Recorder recorder) =>
        batch => IsKeyUp(batch)
            ? recorder.Send(batch)
            : throw new InvalidOperationException("the sender broke");

    /// <summary>
    /// The loop can end without anyone asking it to. Until the engine retired itself here, _cts
    /// stayed set: it went on reporting IsRunning, and the view model was never told, so the
    /// status line said the loop had stopped while the dot stayed green.
    /// </summary>
    [Fact]
    public async Task TheLoopDyingOnItsOwnRetiresTheEngineAndAnnouncesIt()
    {
        var recorder = new Recorder();
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stillRunningWhenAnnounced = true;

        await using var engine = new RepeatEngine { Send = BreaksOnPress(recorder) };
        engine.Stopped += (_, _) =>
        {
            stillRunningWhenAnnounced = engine.IsRunning;
            stopped.TrySetResult();
        };

        engine.Start(X, intervalMs: 1000);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(engine.IsRunning);
        Assert.False(stillRunningWhenAnnounced, "IsRunning must be false before Stopped fires");
        Assert.True(IsKeyUp(recorder.Batches[^1]), "a loop that died must still release the key");
    }

    /// <summary>The user-visible half of the same bug: the app could not be restarted.</summary>
    [Fact]
    public async Task TheEngineStartsAgainAfterTheLoopDiesOnItsOwn()
    {
        var recorder = new Recorder();
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var breaks = BreaksOnPress(recorder);
        var broken = true;

        await using var engine = new RepeatEngine();
        engine.Send = batch => broken ? breaks(batch) : recorder.Send(batch);
        engine.Stopped += (_, _) => stopped.TrySetResult();

        engine.Start(X, intervalMs: 1000);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // With _cts left behind, Start's own IsRunning guard turned this into a no-op and the
        // only way back was to close the app.
        broken = false;
        var beforeSecondStart = recorder.Batches.Length;
        engine.Start(X, intervalMs: 1000);

        // The second run having sent anything is the assertion; waiting for it is how that is
        // observed, rather than sleeping long enough that it has probably happened.
        await WaitForBatchesAsync(recorder, beforeSecondStart + 1);

        Assert.True(engine.IsRunning, "the engine should accept a fresh Start");
    }

    /// <summary>
    /// Stopped means "the loop is no longer running", so a deliberate Stop raises it too — once,
    /// not once per call.
    /// </summary>
    [Fact]
    public async Task StoppingRaisesStoppedExactlyOnce()
    {
        var recorder = new Recorder();
        var count = 0;

        await using var engine = new RepeatEngine { Send = recorder.Send };
        engine.Stopped += (_, _) => Interlocked.Increment(ref count);

        engine.Start(X, intervalMs: 1000);
        await recorder.Started.WaitAsync(TimeSpan.FromMilliseconds(WaitTimeoutMs));
        await engine.StopAsync();
        await engine.StopAsync();

        Assert.Equal(1, count);
        Assert.False(engine.IsRunning);
    }

    /// <summary>A loop that never started has nothing to announce.</summary>
    [Fact]
    public async Task AnEmptyComboRaisesNoStopped()
    {
        var count = 0;

        await using var engine = new RepeatEngine { Send = _ => new SendOutcome(0, 0, 0) };
        engine.Stopped += (_, _) => Interlocked.Increment(ref count);

        engine.Start(default, intervalMs: 50);
        await Task.Delay(60);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task DisposeStopsARunningLoopAndReleasesTheKey()
    {
        var recorder = new Recorder();
        var engine = new RepeatEngine { Send = recorder.Send };

        engine.Start(X, intervalMs: 1000);
        await recorder.Started.WaitAsync(TimeSpan.FromMilliseconds(WaitTimeoutMs));
        await engine.DisposeAsync();

        Assert.False(engine.IsRunning);
        Assert.True(IsKeyUp(recorder.Batches[^1]), "disposing must not leave the key held");
    }
}
