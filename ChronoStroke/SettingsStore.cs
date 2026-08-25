using System.IO;
using System.Security;
using System.Text.Json;

namespace ChronoStroke;

/// <summary>
/// Reads and writes settings.json under %AppData%.
/// </summary>
internal static class SettingsStore
{
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ChronoStroke",
        "settings.json");

    /// <summary>
    /// Loads settings, falling back to defaults for anything that goes wrong.
    /// </summary>
    /// <remarks>
    /// A missing, unreadable, truncated or hand-mangled file must never stop the app from
    /// opening — there is no UI to fix it from if it will not start.
    /// </remarks>
    public static AppSettings Load() => Load(FilePath);

    /// <inheritdoc cref="Load()"/>
    /// <remarks>
    /// The path-taking overload exists so the tests can exercise the fallbacks against a temp
    /// directory. FilePath is resolved once from the real %AppData%, so a round trip through the
    /// parameterless version would overwrite the settings of whoever ran the tests — which is
    /// why the corrupt-file and atomic-write paths went uncovered until this existed.
    /// </remarks>
    internal static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return AppSettings.Default;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, AppSettingsContext.Default.AppSettings)
                   ?? AppSettings.Default;
        }
        // The filter is deliberately wider than the happy path suggests. Path APIs throw
        // NotSupportedException and SecurityException on paths this app would never build itself
        // but a redirected profile folder can produce, and System.Text.Json adds
        // NotSupportedException for a type it cannot read. Anything escaping here is an
        // exception on the way to the window's constructor, which means no window.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or JsonException or ArgumentException
                                       or NotSupportedException or SecurityException)
        {
            return AppSettings.Default;
        }
    }

    /// <returns>Null on success, otherwise a description of what failed.</returns>
    public static string? Save(AppSettings settings) => Save(settings, FilePath);

    /// <inheritdoc cref="Save(AppSettings)"/>
    /// <remarks>See <see cref="Load(string)"/> for why the path-taking overload exists.</remarks>
    internal static string? Save(AppSettings settings, string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Write to a sibling file and swap it in. A crash or a full disk partway through
            // then leaves the previous good settings.json intact rather than a half-written one
            // that fails to parse on next launch.
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, AppSettingsContext.Default.AppSettings));
            File.Move(temp, path, overwrite: true);
            return null;
        }
        // Save runs from a property-changed handler, which runs from a binding setter, which
        // runs on the dispatcher — so anything not caught here surfaces as an unhandled
        // dispatcher exception and takes the app down over a settings write. CreateDirectory
        // contributes NotSupportedException and ArgumentException, and File.Move contributes
        // SecurityException, none of which the previous filter covered.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException
                                       or SecurityException)
        {
            return $"Could not save settings: {ex.Message}";
        }
    }
}
