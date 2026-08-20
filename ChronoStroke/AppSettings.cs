using System.Text.Json.Serialization;
using System.Windows.Input;

namespace ChronoStroke;

/// <summary>
/// What gets written to settings.json. Deliberately a flat record of primitives rather than the
/// live types — the file is a wire format and should not follow refactors of the UI model.
/// </summary>
internal sealed record AppSettings
{
    public ushort SendVirtualKey { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<ModifierKeys>))]
    public ModifierKeys SendModifiers { get; init; }

    public int IntervalMs { get; init; }

    public ushort HotkeyVirtualKey { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<ModifierKeys>))]
    public ModifierKeys HotkeyModifiers { get; init; }

    /// <summary>Send X every 250 ms, toggled with Ctrl+F8.</summary>
    public static AppSettings Default => new()
    {
        SendVirtualKey = 0x58,                      // X
        SendModifiers = ModifierKeys.None,
        IntervalMs = 250,
        HotkeyVirtualKey = 0x77,                    // F8
        HotkeyModifiers = ModifierKeys.Control,
    };

    // Convenience views over the stored primitives. JsonIgnore because otherwise the serializer
    // writes them out as well, duplicating every value into a nested object — plus IsEmpty and
    // DisplayName — in a file that is meant to be legible if anyone opens it.
    [JsonIgnore]
    public KeyCombo SendCombo => new(SendVirtualKey, SendModifiers);

    [JsonIgnore]
    public KeyCombo HotkeyCombo => new(HotkeyVirtualKey, HotkeyModifiers);

    public static AppSettings From(KeyCombo send, KeyCombo hotkey, int intervalMs) => new()
    {
        SendVirtualKey = send.VirtualKey,
        SendModifiers = send.Modifiers,
        IntervalMs = intervalMs,
        HotkeyVirtualKey = hotkey.VirtualKey,
        HotkeyModifiers = hotkey.Modifiers,
    };
}

/// <summary>
/// Source-generated serialization. Reflection-based System.Text.Json works here today, but it is
/// exactly what breaks under single-file publish with any trimming, and the generator costs
/// nothing to use.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsContext : JsonSerializerContext;
