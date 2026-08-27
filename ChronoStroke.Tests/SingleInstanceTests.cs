using ChronoStroke;

namespace ChronoStroke.Tests;

/// <summary>
/// The claim that stops a second copy running.
/// </summary>
/// <remarks>
/// A named mutex is system-wide, so a second TryAcquire from this same process sees exactly what
/// a second process would. Each test uses a name of its own so a parallel run cannot make another
/// test's claim look like its own.
/// </remarks>
public class SingleInstanceTests
{
    private static string UniqueName([System.Runtime.CompilerServices.CallerMemberName] string caller = "") =>
        $@"Local\ChronoStroke-tests-{caller}-{Guid.NewGuid():N}";

    [Fact]
    public void TheFirstClaimSucceeds()
    {
        Assert.True(SingleInstance.TryAcquire(UniqueName(), out var first));
        using (first)
        {
            Assert.NotNull(first);
        }
    }

    [Fact]
    public void ASecondClaimOnTheSameNameIsRefused()
    {
        var name = UniqueName();

        Assert.True(SingleInstance.TryAcquire(name, out var first));
        using (first)
        {
            Assert.False(SingleInstance.TryAcquire(name, out var second));
            Assert.Null(second);
        }
    }

    /// <summary>Closing the first copy has to let the next one start, or the app is unrunnable.</summary>
    [Fact]
    public void TheNameIsFreedWhenTheClaimIsDisposed()
    {
        var name = UniqueName();

        Assert.True(SingleInstance.TryAcquire(name, out var first));
        first!.Dispose();

        Assert.True(SingleInstance.TryAcquire(name, out var second));
        second!.Dispose();
    }

    [Fact]
    public void DifferentNamesDoNotCollide()
    {
        Assert.True(SingleInstance.TryAcquire(UniqueName() + "-a", out var a));
        using (a)
        {
            Assert.True(SingleInstance.TryAcquire(UniqueName() + "-b", out var b));
            b!.Dispose();
        }
    }

    /// <summary>
    /// A refused claim must not leave its own handle open, or the name would stay taken after the
    /// first copy closed and no copy could ever start again.
    /// </summary>
    [Fact]
    public void ARefusedClaimDoesNotKeepTheNameAlive()
    {
        var name = UniqueName();

        Assert.True(SingleInstance.TryAcquire(name, out var first));
        Assert.False(SingleInstance.TryAcquire(name, out _));
        Assert.False(SingleInstance.TryAcquire(name, out _));

        first!.Dispose();

        Assert.True(SingleInstance.TryAcquire(name, out var next));
        next!.Dispose();
    }
}
