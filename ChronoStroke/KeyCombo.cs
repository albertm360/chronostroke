using System.Windows.Input;
using ChronoStroke.Interop;

namespace ChronoStroke;

/// <summary>
/// A key plus its modifiers, e.g. "Ctrl+Shift+E".
/// </summary>
/// <remarks>
/// Reuses WPF's <see cref="ModifierKeys"/> rather than defining a parallel enum — this is a WPF
/// app, the capture UI hands us that type directly, and it maps cleanly onto both the virtual
/// keys SendInput wants and the MOD_* flags RegisterHotKey wants.
/// </remarks>
internal readonly record struct KeyCombo(ushort VirtualKey, ModifierKeys Modifiers)
{
    /// <summary>Highest value in the virtual-key table — VK_OEM_CLEAR.</summary>
    private const ushort MaxVirtualKey = 0xFE;

    /// <summary>The four modifiers WPF defines. Anything else in the field is not a modifier.</summary>
    private const ModifierKeys KnownModifiers =
        ModifierKeys.Alt | ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Windows;

    public bool IsEmpty => VirtualKey == 0;

    /// <summary>
    /// Returns this combination if the app can act on it, and an empty one if it cannot.
    /// </summary>
    /// <remarks>
    /// This exists for values arriving from <c>settings.json</c>, which is plain text in a folder
    /// the user can open. The interval and the step were already clamped on load; these two fields
    /// were not, while the README claimed every value was re-validated. Nothing dangerous got
    /// through — a nonsense virtual key falls into the virtual-key branch of the sender and
    /// SendInput refuses it — but "mostly validated" is not what the file's readers were promised.
    /// <para>
    /// A key past the end of the table cannot be sent at all, so the whole combination is dropped
    /// rather than kept with a key that does nothing. Undefined bits in the modifiers are cleared
    /// instead: the key itself is still good, and Ctrl+X with a stray bit is obviously meant to be
    /// Ctrl+X. Reserved and unassigned codes inside the table are left alone — enumerating them
    /// would be a second table to keep in step with Windows, for keys that already fail harmlessly.
    /// </para>
    /// </remarks>
    public KeyCombo Sanitised() =>
        VirtualKey is 0 or > MaxVirtualKey
            ? default
            : new KeyCombo(VirtualKey, Modifiers & KnownModifiers);

    /// <summary>Human-readable form, e.g. "Ctrl+Shift+E". Modifier order is fixed so that two
    /// equal combos always render identically.</summary>
    public string DisplayName
    {
        get
        {
            if (IsEmpty)
            {
                return string.Empty;
            }

            var parts = new List<string>(5);
            if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            parts.Add(KeyName(VirtualKey));
            return string.Join('+', parts);
        }
    }

    /// <summary>
    /// Friendly name for a single key. The <see cref="Key"/> enum names are unhelpfully literal
    /// — the comma key is "OemComma" and the 1 key is "D1" — so where a key has a printable
    /// character we ask Windows for it instead.
    /// </summary>
    private static string KeyName(ushort vk)
    {
        // Numpad keys are deliberately excluded: MAPVK_VK_TO_CHAR maps Numpad4 to '4', which
        // would render identically to the top-row 4 while behaving as a completely different
        // key. "NumPad4" is uglier and correct.
        var isNumpad = vk is >= 0x60 and <= 0x6F;
        if (!isNumpad)
        {
            var mapped = NativeMethods.MapVirtualKeyW(vk, NativeMethods.MAPVK_VK_TO_CHAR);
            // The dead-key flag is bit 31 of the DWORD, not the top bit of the character, so
            // the character is the low 16 bits. Masking to 0x7FFF truncated it to 15.
            var ch = (char)(mapped & 0xFFFF);
            if (ch != '\0' && !char.IsControl(ch) && !char.IsWhiteSpace(ch))
            {
                return char.ToUpperInvariant(ch).ToString();
            }
        }

        return KeyInterop.KeyFromVirtualKey(vk).ToString();
    }

    public override string ToString() => DisplayName;
}
