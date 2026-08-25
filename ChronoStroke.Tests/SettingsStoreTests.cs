using System.IO;
using System.Windows.Input;
using ChronoStroke;

namespace ChronoStroke.Tests;

/// <summary>
/// The load fallbacks and the atomic write, exercised against a temp directory.
/// </summary>
/// <remarks>
/// These could not be written until <see cref="SettingsStore"/> grew path-taking overloads:
/// <c>FilePath</c> resolves once from the real %AppData%, so a round trip through the public
/// members would overwrite the settings of whoever ran the tests. What is covered here is the
/// promise the class exists to keep — a missing, truncated or hand-mangled file must never stop
/// the app from opening, because there is no UI to fix it from if it will not start.
/// </remarks>
public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "ChronoStrokeTests", Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void AMissingFileLoadsTheDefaults()
    {
        Assert.Equal(AppSettings.Default, SettingsStore.Load(Path_));
    }

    /// <summary>
    /// The file sits in a folder the user can open, so it will eventually be edited by hand and
    /// broken. Every one of these has to come back as defaults rather than an exception on the
    /// way to the window's constructor.
    /// </summary>
    [Theory]
    [InlineData("", "empty")]
    [InlineData("   ", "whitespace")]
    [InlineData("not json at all", "garbage")]
    [InlineData("{\"SendVirtualKey\": 88,", "truncated mid-object")]
    [InlineData("[]", "an array where an object belongs")]
    [InlineData("null", "a literal null")]
    [InlineData("{\"SendVirtualKey\": \"eighty-eight\"}", "wrong type for a field")]
    public void AMangledFileLoadsTheDefaults(string contents, string why)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path_, contents);

        Assert.True(
            AppSettings.Default == SettingsStore.Load(Path_),
            $"a file that is {why} should load the defaults");
    }

    [Fact]
    public void SavedSettingsComeBackFromDisk()
    {
        var settings = AppSettings.From(
            new KeyCombo(0x58, ModifierKeys.None),
            new KeyCombo(0x77, ModifierKeys.Control),
            intervalMs: 300,
            intervalStepMs: 25);

        Assert.Null(SettingsStore.Save(settings, Path_));
        Assert.Equal(settings, SettingsStore.Load(Path_));
    }

    [Fact]
    public void SavingCreatesTheDirectoryItNeeds()
    {
        Assert.False(Directory.Exists(_dir));

        Assert.Null(SettingsStore.Save(AppSettings.Default, Path_));

        Assert.True(File.Exists(Path_));
    }

    /// <summary>
    /// The write goes to a sibling file and is renamed in, so a crash partway through leaves the
    /// previous settings.json rather than a half-written one. What is checkable afterwards is
    /// that the sibling does not survive a successful write — a stray .tmp would mean the rename
    /// never happened and the real file was written directly.
    /// </summary>
    [Fact]
    public void TheTempFileDoesNotSurviveASuccessfulWrite()
    {
        Assert.Null(SettingsStore.Save(AppSettings.Default, Path_));

        Assert.True(File.Exists(Path_));
        Assert.False(File.Exists(Path_ + ".tmp"));
    }

    [Fact]
    public void SavingTwiceOverwritesRatherThanFailing()
    {
        var first = AppSettings.From(default, default, intervalMs: 100, intervalStepMs: 5);
        var second = AppSettings.From(default, default, intervalMs: 900, intervalStepMs: 50);

        Assert.Null(SettingsStore.Save(first, Path_));
        Assert.Null(SettingsStore.Save(second, Path_));

        Assert.Equal(second, SettingsStore.Load(Path_));
    }

    /// <summary>
    /// Save runs from a property-changed handler on the dispatcher, so a throw here takes the app
    /// down over a settings write. A directory standing where the file should be is the cheapest
    /// way to make the write fail without special permissions.
    /// </summary>
    [Fact]
    public void AnUnwritablePathIsReportedRatherThanThrown()
    {
        Directory.CreateDirectory(Path_);

        var error = SettingsStore.Save(AppSettings.Default, Path_);

        Assert.NotNull(error);
        Assert.Contains("Could not save settings", error);
    }

    /// <summary>A directory in place of the file must not stop the app opening either.</summary>
    [Fact]
    public void AnUnreadablePathLoadsTheDefaults()
    {
        Directory.CreateDirectory(Path_);

        Assert.Equal(AppSettings.Default, SettingsStore.Load(Path_));
    }
}
