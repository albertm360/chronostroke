using ChronoStroke;

namespace ChronoStroke.Tests;

/// <summary>
/// The bounded box the interval and the step are both instances of.
/// </summary>
/// <remarks>
/// The validators were already covered through the view model's two static wrappers. What was
/// not, and could not be while this logic was six duplicated member pairs inside a view model
/// whose constructor reads the user's own settings file, is everything around them: which value
/// the box remembers when its contents stop being usable, what clamping does on load, and where
/// nudging stops.
/// </remarks>
public class BoundedIntFieldTests
{
    private static BoundedIntField Interval() => MainViewModel.CreateIntervalField();

    private static BoundedIntField Step() => MainViewModel.CreateStepField();

    [Fact]
    public void ANewFieldStartsOnItsInitialValue()
    {
        var field = Interval();

        Assert.Equal(AppSettings.Default.IntervalMs.ToString(), field.Text);
        Assert.Equal(AppSettings.Default.IntervalMs, field.LastValid);
        Assert.Null(field.Error);
    }

    /// <summary>
    /// The point of the last-good value. Typing over a usable number leaves the box unusable for
    /// as long as it takes to finish typing, and the arrows and the saved settings both have to
    /// keep working through that.
    /// </summary>
    [Fact]
    public void AnUnusableValueDoesNotDisturbTheLastGoodOne()
    {
        var field = Interval();
        field.Text = "300";
        Assert.Equal(300, field.LastValid);

        field.Text = "";
        Assert.NotNull(field.Error);
        Assert.Equal(300, field.LastValid);
        Assert.Equal(300, field.ValueOrLastValid);

        field.Text = "3";                       // below the floor, still not usable
        Assert.NotNull(field.Error);
        Assert.Equal(300, field.LastValid);

        field.Text = "30000";                   // usable again
        Assert.Null(field.Error);
        Assert.Equal(30000, field.LastValid);
    }

    [Fact]
    public void ValueOrLastValidPrefersTheBoxWhenItIsUsable()
    {
        var field = Interval();
        field.Text = "450";

        Assert.Equal(450, field.ValueOrLastValid);
    }

    [Theory]
    [InlineData(1, MainViewModel.MinIntervalMs)]        // under the floor
    [InlineData(999_999, MainViewModel.MaxIntervalMs)]  // over the ceiling
    [InlineData(300, 300)]                              // untouched in range
    public void SetClampedForcesTheValueInsideTheBounds(int given, int expected)
    {
        var field = Interval();

        field.SetClamped(given);

        Assert.Equal(expected, field.LastValid);
        Assert.Equal(expected.ToString(), field.Text);
        Assert.Null(field.Error);
    }

    [Fact]
    public void NudgingMovesByTheAmountGiven()
    {
        var field = Interval();
        field.SetClamped(300);

        field.Nudge(25);
        Assert.Equal(325, field.LastValid);

        field.Nudge(-100);
        Assert.Equal(225, field.LastValid);
    }

    /// <summary>
    /// Holding an arrow down walks into the guard rail rather than through it. The commands
    /// disable at the limit, but the clamp is what makes that safe rather than merely tidy.
    /// </summary>
    [Fact]
    public void NudgingStopsAtTheGuardRails()
    {
        var field = Interval();

        field.SetClamped(MainViewModel.MinIntervalMs);
        field.Nudge(-1000);
        Assert.Equal(MainViewModel.MinIntervalMs, field.LastValid);

        field.SetClamped(MainViewModel.MaxIntervalMs);
        field.Nudge(1000);
        Assert.Equal(MainViewModel.MaxIntervalMs, field.LastValid);
    }

    /// <summary>
    /// Nudging from a box that has nothing usable in it counts from the last good value, so the
    /// arrows keep working while a number is half-typed.
    /// </summary>
    [Fact]
    public void NudgingFromAnUnusableBoxCountsFromTheLastGoodValue()
    {
        var field = Interval();
        field.SetClamped(300);
        field.Text = "not a number";

        field.Nudge(10);

        Assert.Equal(310, field.LastValid);
        Assert.Equal("310", field.Text);
    }

    [Fact]
    public void ChangedFiresForEveryEditIncludingUnusableOnes()
    {
        var field = Interval();
        var count = 0;
        field.Changed += (_, _) => count++;

        field.Text = "3";           // unusable, but the save path still needs to hear about it
        field.Text = "30";          // still unusable
        field.Text = "300";         // usable

        Assert.Equal(3, count);
    }

    /// <summary>
    /// Changed fires after the last-good value has caught up, not before — a handler that saves
    /// settings reads LastValid, and would otherwise persist the previous number.
    /// </summary>
    [Fact]
    public void ChangedFiresAfterTheLastGoodValueHasCaughtUp()
    {
        var field = Interval();
        var seen = 0;
        field.Changed += (_, _) => seen = field.LastValid;

        field.Text = "777";

        Assert.Equal(777, seen);
    }

    [Fact]
    public void TheTwoFieldsCarryTheirOwnBounds()
    {
        Assert.Equal(MainViewModel.MinIntervalMs, Interval().Min);
        Assert.Equal(MainViewModel.MaxIntervalMs, Interval().Max);
        Assert.Equal(MainViewModel.MinStepMs, Step().Min);
        Assert.Equal(MainViewModel.MaxStepMs, Step().Max);
    }

    /// <summary>
    /// The interval's floor explains itself; the step's range does not need to. Losing that in
    /// the extraction would have been a quiet downgrade, so it is asserted rather than assumed.
    /// </summary>
    [Fact]
    public void TheIntervalFloorStillExplainsWhyItExists()
    {
        var field = Interval();
        field.Text = "1";

        Assert.Contains("floods the input queue", field.Error);
    }

    [Fact]
    public void TheStepUsesOneMessageForBothEnds()
    {
        var field = Step();

        field.Text = "0";
        var below = field.Error;
        field.Text = "5000";
        var above = field.Error;

        Assert.Equal(below, above);
        Assert.Contains("between", below);
    }
}
