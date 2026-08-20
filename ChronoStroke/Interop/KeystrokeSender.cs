using System.Runtime.InteropServices;
using System.Windows.Input;

namespace ChronoStroke.Interop;

/// <summary>How a SendInput call went. Nothing is swallowed — the UI shows failures.</summary>
internal readonly record struct SendOutcome(int Expected, uint Inserted, int LastError)
{
    public bool Success => Inserted == Expected;

    public string Describe() => Success
        ? "OK"
        : $"SendInput inserted {Inserted} of {Expected} events (Win32 error {LastError}).";
}

/// <summary>
/// Turns a <see cref="KeyCombo"/> into SendInput batches.
/// </summary>
internal static class KeystrokeSender
{
    /// <summary>Presses the modifiers then the key, in one atomic batch.</summary>
    public static SendOutcome Press(KeyCombo combo) => Send(combo, keyUp: false);

    /// <summary>Releases the key then the modifiers — reverse order, one atomic batch.</summary>
    public static SendOutcome Release(KeyCombo combo) => Send(combo, keyUp: true);

    private static SendOutcome Send(KeyCombo combo, bool keyUp)
    {
        if (combo.IsEmpty)
        {
            return new SendOutcome(0, 0, 0);
        }

        // Press order is modifiers-then-key, which is what physically happens when you hold
        // Ctrl and tap X. Release runs the same list backwards so the key comes up before the
        // modifiers do — releasing Ctrl first would briefly leave a bare X held down.
        Span<ushort> keys = stackalloc ushort[5];
        var count = 0;
        if (combo.Modifiers.HasFlag(ModifierKeys.Control)) keys[count++] = NativeMethods.VK_CONTROL;
        if (combo.Modifiers.HasFlag(ModifierKeys.Shift)) keys[count++] = NativeMethods.VK_SHIFT;
        if (combo.Modifiers.HasFlag(ModifierKeys.Alt)) keys[count++] = NativeMethods.VK_MENU;
        if (combo.Modifiers.HasFlag(ModifierKeys.Windows)) keys[count++] = NativeMethods.VK_LWIN;
        keys[count++] = combo.VirtualKey;

        var inputs = new NativeMethods.INPUT[count];
        for (var i = 0; i < count; i++)
        {
            // keyUp walks the list backwards.
            var vk = keys[keyUp ? count - 1 - i : i];
            inputs[i] = BuildKeyEvent(vk, keyUp);
        }

        // One call for the whole batch. The docs guarantee events from a single SendInput call
        // are inserted serially and are NOT interleaved with real keyboard input or with other
        // SendInput calls — so a combo can never be torn apart halfway through.
        var inserted = NativeMethods.SendInput((uint)count, ref inputs[0], NativeMethods.InputSize);
        var error = inserted == count ? 0 : Marshal.GetLastWin32Error();
        return new SendOutcome(count, inserted, error);
    }

    private static NativeMethods.INPUT BuildKeyEvent(ushort virtualKey, bool keyUp) =>
        new()
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion { ki = BuildKeyEventData(virtualKey, keyUp) },
        };

    /// <summary>
    /// Builds one key event. Exposed as internal rather than private so the flag decisions
    /// below can be verified directly, without actually injecting input.
    /// </summary>
    internal static NativeMethods.KEYBDINPUT BuildKeyEventData(ushort virtualKey, bool keyUp)
    {
        var ki = new NativeMethods.KEYBDINPUT
        {
            time = 0,           // 0 = let the system stamp it
            dwExtraInfo = 0,
        };

        // Scan codes, not virtual keys. A virtual key is a logical, layout-dependent idea of
        // "which key"; a scan code is what the hardware actually puts on the wire. Games that
        // read input through DirectInput or Raw Input see scan codes and frequently ignore
        // virtual-key-only injected events entirely — which is the whole reason this app exists
        // rather than a two-line SendKeys.Send call.
        var scan = NativeMethods.MapVirtualKeyW(virtualKey, NativeMethods.MAPVK_VK_TO_VSC_EX);
        var prefix = scan >> 8;

        // A 0xE1 prefix means VK_PAUSE, whose real scan sequence is two events (0xE1 0x1D 0x45).
        // There is no honest way to express that as a single event: taking the low byte would
        // send 0x1D, which is Left Ctrl. Fall back to virtual-key mode instead of sending a lie.
        var usable = scan != 0 && prefix != 0xE1;

        if (usable)
        {
            // With KEYEVENTF_SCANCODE set, the docs say wVk is ignored outright, so leave it 0
            // rather than filling it with something misleading.
            ki.wVk = 0;
            ki.wScan = (ushort)(scan & 0xFF);
            ki.dwFlags = NativeMethods.KEYEVENTF_SCANCODE;

            if (prefix == 0xE0 || IsAlwaysExtended(virtualKey))
            {
                ki.dwFlags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
            }
        }
        else
        {
            // No usable scan code for this key on the current layout. Fall back to virtual-key
            // mode for this one event: less likely to work in a game, but better than sending
            // a scan code that means a different key.
            ki.wVk = virtualKey;
            ki.wScan = 0;
            ki.dwFlags = 0;
        }

        if (keyUp)
        {
            ki.dwFlags |= NativeMethods.KEYEVENTF_KEYUP;
        }

        return ki;
    }

    /// <summary>
    /// Virtual keys that are physically extended (0xE0-prefixed) but which MapVirtualKeyW does
    /// NOT report as such — it hands back the bare scan code with no prefix.
    /// </summary>
    /// <remarks>
    /// This list is not defensive padding; it was derived by running MapVirtualKeyW over every
    /// candidate. Each key below shares its scan code exactly with a numpad key, and the
    /// extended flag is the only thing that separates them:
    /// <code>
    ///   Home 0x47 = Numpad7      Up    0x48 = Numpad8     PageUp   0x49 = Numpad9
    ///   Left 0x4B = Numpad4      Right 0x4D = Numpad6     End      0x4F = Numpad1
    ///   Down 0x50 = Numpad2      PgDn  0x51 = Numpad3     Insert   0x52 = Numpad0
    ///   Delete 0x53 = Decimal    NumLock 0x45 = Pause
    /// </code>
    /// Omit the flag and picking "Left Arrow" silently sends Numpad 4.
    /// MapVirtualKeyW DOES report the prefix for Right Ctrl/Alt, Win, Apps and numpad divide,
    /// so those are deliberately absent here — the 0xE0 check above already catches them.
    /// </remarks>
    private static bool IsAlwaysExtended(ushort vk) => vk switch
    {
        0x21 or 0x22 => true,                     // Page Up / Page Down
        0x23 or 0x24 => true,                     // End / Home
        0x25 or 0x26 or 0x27 or 0x28 => true,     // Left / Up / Right / Down
        0x2D or 0x2E => true,                     // Insert / Delete
        0x90 => true,                             // Num Lock
        _ => false,
    };
}
