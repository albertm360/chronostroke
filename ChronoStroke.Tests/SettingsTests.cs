using System.Text.Json;
using System.Windows.Input;
using ChronoStroke;

namespace ChronoStroke.Tests;

/// <summary>
/// The settings shape and the interval guard rails.
/// </summary>
/// <remarks>
/// Nothing here touches the disk. <see cref="SettingsStore"/> writes to a fixed path under the
/// real %AppData%, so a round-trip test through it would overwrite the settings of whoever ran
/// the tests. The serialization is exercised through the same source-generated context the store
/// uses, which is the part that can actually break.
/// </remarks>
public class SettingsTests
{
    private static readonly AppSettings Sample = AppSettings.From(
        new KeyCombo(0x58, ModifierKeys.None),                          // X
        new KeyCombo(0x77, ModifierKeys.Control | ModifierKeys.Shift),  // Ctrl+Shift+F8
        300,
        5);

    [Fact]
    public void SettingsSurviveARoundTrip()
    {
        var json = JsonSerializer.Serialize(Sample, AppSettingsContext.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, AppSettingsContext.Default.AppSettings);

        Assert.Equal(Sample, back);
    }

    /// <summary>
    /// Modifiers are written as names rather than as the flags enum's numeric value, so the file
    /// stays legible to anyone who opens it — and so a future renumbering cannot silently change
    /// what an existing file means.
    /// </summary>
    [Fact]
    public void ModifiersArePersistedByName()
    {
        var json = JsonSerializer.Serialize(Sample, AppSettingsContext.Default.AppSettings);

        Assert.Contains("Control", json);
        Assert.Contains("Shift", json);
    }

    /// <summary>
    /// The convenience views must not be written out; they would duplicate every value into a
    /// nested object in a file meant to be readable.
    /// </summary>
    [Fact]
    public void ComboViewsAreNotSerialized()
    {
        var json = JsonSerializer.Serialize(Sample, AppSettingsContext.Default.AppSettings);

        Assert.DoesNotContain("SendCombo", json);
        Assert.DoesNotContain("HotkeyCombo", json);
        Assert.DoesNotContain("DisplayName", json);
    }

    [Fact]
    public void ComboViewsReadBackWhatWasStored()
    {
        Assert.Equal(new KeyCombo(0x58, ModifierKeys.None), Sample.SendCombo);
        Assert.Equal(new KeyCombo(0x77, ModifierKeys.Control | ModifierKeys.Shift), Sample.HotkeyCombo);
    }

    [Theory]
    [InlineData("250", 250)]
    [InlineData("50", MainViewModel.MinIntervalMs)]
    [InlineData("60000", MainViewModel.MaxIntervalMs)]
    [InlineData("  250  ", 250)]        // trimmed before parsing
    public void ValidIntervalsAreAccepted(string text, int expected)
    {
        Assert.Null(MainViewModel.ValidateInterval(text, out var value));
        Assert.Equal(expected, value);
    }

    /// <summary>
    /// The floor is the guard rail that matters: below it the machine takes input faster than
    /// most windows can drain it, and recovering means killing the process.
    /// </summary>
    [Theory]
    [InlineData("49")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("60001")]
    [InlineData("-250")]        // NumberStyles.None rejects the sign
    [InlineData("2.5")]
    [InlineData("250 ms")]
    [InlineData("")]
    [InlineData(null)]
    public void UnusableIntervalsAreRejectedWithAReason(string? text)
    {
        var error = MainViewModel.ValidateInterval(text, out var value);

        Assert.NotNull(error);
        Assert.NotEqual(string.Empty, error);
        Assert.Equal(0, value);
    }

    [Theory]
    [InlineData("10", 10)]
    [InlineData("1", MainViewModel.MinStepMs)]
    [InlineData("1000", MainViewModel.MaxStepMs)]
    [InlineData("  5  ", 5)]            // trimmed before parsing
    public void ValidStepsAreAccepted(string text, int expected)
    {
        Assert.Null(MainViewModel.ValidateStep(text, out var value));
        Assert.Equal(expected, value);
    }

    /// <summary>
    /// A zero step would make the arrows do nothing, and the rest are the same parsing failures
    /// the interval box rejects.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("1001")]
    [InlineData("-5")]                  // NumberStyles.None rejects the sign
    [InlineData("2.5")]
    [InlineData("5 ms")]
    [InlineData("")]
    [InlineData(null)]
    public void UnusableStepsAreRejectedWithAReason(string? text)
    {
        var error = MainViewModel.ValidateStep(text, out var value);

        Assert.NotNull(error);
        Assert.NotEqual(string.Empty, error);
        Assert.Equal(0, value);
    }

    [Fact]
    public void DefaultsAreWithinTheAppsOwnLimits()
    {
        var defaults = AppSettings.Default;

        Assert.InRange(defaults.IntervalMs, MainViewModel.MinIntervalMs, MainViewModel.MaxIntervalMs);
        Assert.InRange(defaults.IntervalStepMs, MainViewModel.MinStepMs, MainViewModel.MaxStepMs);
        Assert.False(defaults.SendCombo.IsEmpty);
        Assert.False(defaults.HotkeyCombo.IsEmpty);
        Assert.NotEqual(defaults.SendCombo, defaults.HotkeyCombo);
    }
}
