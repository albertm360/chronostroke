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
public readonly record struct KeyCombo(ushort VirtualKey, ModifierKeys Modifiers)
{
    public bool IsEmpty => VirtualKey == 0;

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
            var ch = (char)(mapped & 0x7FFF);   // top bit flags a dead key
            if (ch != '\0' && !char.IsControl(ch) && !char.IsWhiteSpace(ch))
            {
                return char.ToUpperInvariant(ch).ToString();
            }
        }

        return KeyInterop.KeyFromVirtualKey(vk).ToString();
    }

    public override string ToString() => DisplayName;
}
