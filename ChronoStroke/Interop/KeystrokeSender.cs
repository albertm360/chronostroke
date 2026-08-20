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
    /// <summary>
    /// Builds the batch for one combination: <paramref name="keyUp"/> false presses the
    /// modifiers then the key, true releases them in reverse.
    /// </summary>
    /// <remarks>
    /// Call this from the UI thread and reuse the result. MapVirtualKeyW below resolves against
    /// the *calling thread's* active keyboard layout (GetKeyboardLayout(0)), and the repeat loop
    /// runs on the thread pool, where that is the process default rather than the layout the
    /// user was on when they captured the key. On a QWERTY-family layout the two tables agree,
    /// so the difference is invisible until someone on AZERTY or Dvorak runs the app and gets
    /// the wrong key — the worst kind of bug to diagnose after the fact.
    /// Building once also keeps the loop free of P/Invokes and allocations: the combination is
    /// fixed for the whole run, but resolving per tick meant up to ten MapVirtualKeyW calls and
    /// two arrays every time, forty times a second at the 50 ms floor.
    /// </remarks>
    public static NativeMethods.INPUT[] BuildBatch(KeyCombo combo, bool keyUp)
    {
        if (combo.IsEmpty)
        {
            return [];
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

        return inputs;
    }

    /// <summary>
    /// Sends a batch built by <see cref="BuildBatch"/>. Safe to call from any thread — every
    /// layout-sensitive decision was already made when the batch was built.
    /// </summary>
    public static SendOutcome Send(NativeMethods.INPUT[] batch)
    {
        if (batch.Length == 0)
        {
            return new SendOutcome(0, 0, 0);
        }

        // One call for the whole batch. The docs guarantee events from a single SendInput call
        // are inserted serially and are NOT interleaved with real keyboard input or with other
        // SendInput calls — so a combo can never be torn apart halfway through.
        var inserted = NativeMethods.SendInput((uint)batch.Length, ref batch[0], NativeMethods.InputSize);
        var error = inserted == batch.Length ? 0 : Marshal.GetLastWin32Error();
        return new SendOutcome(batch.Length, inserted, error);
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
    ///   Delete 0x53 = Decimal
    /// </code>
    /// Omit the flag and picking "Left Arrow" silently sends Numpad 4.
    /// MapVirtualKeyW DOES report the prefix for Right Ctrl/Alt, Win, Apps, numpad divide and
    /// the browser/media keys, so those are deliberately absent here — the 0xE0 check above
    /// already catches them, and adding them would double-flag nothing but confusion.
    /// Num Lock does not belong here either: it maps to a bare 0x45 and nothing else claims
    /// that code (Pause is 0xE11D, which takes the virtual-key fallback above).
    /// </remarks>
    private static bool IsAlwaysExtended(ushort vk) => vk switch
    {
        0x21 or 0x22 => true,                     // Page Up / Page Down
        0x23 or 0x24 => true,                     // End / Home
        0x25 or 0x26 or 0x27 or 0x28 => true,     // Left / Up / Right / Down
        0x2D or 0x2E => true,                     // Insert / Delete
        _ => false,
    };
}
