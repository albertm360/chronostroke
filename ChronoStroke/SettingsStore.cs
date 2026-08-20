using System.IO;
using System.Text.Json;

namespace ChronoStroke;

/// <summary>
/// Reads and writes settings.json under %AppData%.
/// </summary>
public static class SettingsStore
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
    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return AppSettings.Default;
            }

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize(json, AppSettingsContext.Default.AppSettings)
                   ?? AppSettings.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or JsonException or ArgumentException)
        {
            return AppSettings.Default;
        }
    }

    /// <returns>Null on success, otherwise a description of what failed.</returns>
    public static string? Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            // Write to a sibling file and swap it in. A crash or a full disk partway through
            // then leaves the previous good settings.json intact rather than a half-written one
            // that fails to parse on next launch.
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, AppSettingsContext.Default.AppSettings));
            File.Move(temp, FilePath, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Could not save settings: {ex.Message}";
        }
    }
}
