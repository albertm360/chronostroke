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

    /// <summary>Records every batch the loop sends and reports success, as SendInput would.</summary>
    private sealed class Recorder
    {
        private readonly ConcurrentQueue<NativeMethods.INPUT[]> _batches = new();

        public NativeMethods.INPUT[][] Batches => [.. _batches];

        public SendOutcome Send(NativeMethods.INPUT[] batch)
        {
            _batches.Enqueue(batch);
            return new SendOutcome(batch.Length, (uint)batch.Length, 0);
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

        // Interval 1000 gives the full 40 ms hold; stopping after 15 ms lands inside it.
        engine.Start(X, intervalMs: 1000);
        await Task.Delay(15);
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
        await Task.Delay(200);
        await engine.StopAsync();

        var batches = recorder.Batches;
        Assert.True(batches.Length >= 4, $"expected several cycles, saw {batches.Length} batches");
        Assert.True(IsKeyUp(batches[^1]), "the key must be up after the loop unwinds");
    }

    [Fact]
    public async Task PressesAndReleasesAlternateWhileTheLoopRuns()
    {
        var recorder = new Recorder();
        await using var engine = new RepeatEngine { Send = recorder.Send };

        engine.Start(X, intervalMs: 50);
        await Task.Delay(200);
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
        await using var engine = new RepeatEngine
        {
            // Nothing inserted, with a plausible Win32 error — what a blocked SendInput looks like.
            Send = batch => new SendOutcome(batch.Length, 0, 5),
        };
        engine.SendFailed += (_, message) => reports.Enqueue(message);

        engine.Start(X, intervalMs: 50);
        await Task.Delay(250);
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
        await Task.Delay(15);
        await engine.StopAsync();

        // One press from the single loop, one release from its finally. A second loop would have
        // pressed again.
        Assert.Equal(2, recorder.Batches.Length);
    }

    [Fact]
    public async Task DisposeStopsARunningLoopAndReleasesTheKey()
    {
        var recorder = new Recorder();
        var engine = new RepeatEngine { Send = recorder.Send };

        engine.Start(X, intervalMs: 1000);
        await Task.Delay(15);
        await engine.DisposeAsync();

        Assert.False(engine.IsRunning);
        Assert.True(IsKeyUp(recorder.Batches[^1]), "disposing must not leave the key held");
    }
}
