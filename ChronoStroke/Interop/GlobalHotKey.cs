using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace ChronoStroke.Interop;

/// <summary>
/// One system-wide hotkey registration, as <see cref="HotkeyBinder"/> needs it.
/// </summary>
/// <remarks>
/// The seam exists so the register-fail-roll-back-re-register sequence can be tested. Registering
/// for real needs a window handle and takes the key away from the whole machine while it lasts,
/// so the alternative was leaving the subtlest state machine in the app uncovered — which is what
/// happened until this existed. <see cref="GlobalHotKey"/> is the only implementation that ships.
/// </remarks>
internal interface IHotKeyRegistration : IDisposable
{
    /// <summary>The currently registered combination, if any.</summary>
    KeyCombo Current { get; }

    /// <summary>Registers <paramref name="combo"/>, replacing any existing registration.</summary>
    /// <returns>True on success; otherwise false with <paramref name="error"/> explaining why.</returns>
    bool TryRegister(KeyCombo combo, [NotNullWhen(false)] out string? error);

    /// <summary>Drops the current registration, if there is one.</summary>
    void Unregister();
}

/// <summary>
/// Owns one system-wide hotkey registration for a window.
/// </summary>
/// <remarks>
/// The window handle must outlive this object — WM_HOTKEY is posted to that window's message
/// queue, and unregistering after the HWND is destroyed is pointless. In practice the window
/// disposes this during OnClosing, while its handle is still valid.
/// </remarks>
internal sealed class GlobalHotKey(IntPtr windowHandle) : IHotKeyRegistration
{
    private bool _registered;

    /// <summary>The currently registered combination, if any.</summary>
    public KeyCombo Current { get; private set; }

    /// <summary>
    /// Registers <paramref name="combo"/>, replacing any existing registration.
    /// </summary>
    /// <returns>True on success; otherwise false with <paramref name="error"/> explaining why.</returns>
    public bool TryRegister(KeyCombo combo, [NotNullWhen(false)] out string? error)
    {
        Unregister();

        if (combo.IsEmpty)
        {
            error = "No hotkey set.";
            return false;
        }

        if (combo.VirtualKey == NativeMethods.VK_F12)
        {
            // Reserved for the debugger at all times, even when nothing is being debugged.
            error = "F12 is reserved by the debugger and cannot be a hotkey.";
            return false;
        }

        if (combo.Modifiers == ModifierKeys.None && !IsSafeWithoutModifiers(combo.VirtualKey))
        {
            error = $"{combo.DisplayName} needs a modifier. A hotkey is taken by the system "
                  + "before the focused window sees it, so on its own it would stop working "
                  + "everywhere else while ChronoStroke is open.";
            return false;
        }

        if (!NativeMethods.RegisterHotKey(
                windowHandle,
                NativeMethods.HotKeyId,
                ToWin32Modifiers(combo.Modifiers) | NativeMethods.MOD_NOREPEAT,
                combo.VirtualKey))
        {
            var code = Marshal.GetLastWin32Error();
            error = code == NativeMethods.ErrorHotKeyAlreadyRegistered
                ? $"{combo.DisplayName} is already in use by another application."
                : $"Could not register {combo.DisplayName} (Win32 error {code}).";
            return false;
        }

        _registered = true;
        Current = combo;
        error = null;
        return true;
    }

    public void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(windowHandle, NativeMethods.HotKeyId);
        _registered = false;
        Current = default;
    }

    /// <summary>
    /// True for the handful of keys that can be registered on their own without taking a key
    /// the user needs for ordinary typing.
    /// </summary>
    /// <remarks>
    /// A registered hotkey is consumed by the system rather than delivered to the focused
    /// window, so a bare registration removes that key from every application on the machine
    /// for as long as this app is open. Bind Space and the space bar stops working everywhere;
    /// bind Enter and it is worse. It is recoverable — a different key still reaches the capture
    /// box — but nothing on screen connects the cause to the effect while it lasts.
    /// <para>
    /// The obvious derivation, "does MapVirtualKeyW with MAPVK_VK_TO_CHAR return a printable
    /// character", does not work. Measured on this machine: Space returns ' ' and Enter 0x0D and
    /// Tab 0x09, so a printable-character test rejects them as whitespace or control characters
    /// and lets all three through; the arrows, Home, Delete and Insert return 0, which is
    /// exactly what F8 and Pause return. The call cannot separate the dangerous keys from the
    /// safe ones, and it varies by keyboard layout besides.
    /// </para>
    /// <para>
    /// So the safe set is named outright, and it fails closed: a key that is not on the list
    /// needs a modifier. Being too strict costs the user one extra key press when choosing a
    /// hotkey; being too loose costs them a key that no longer works anywhere.
    /// </para>
    /// </remarks>
    internal static bool IsSafeWithoutModifiers(ushort vk) =>
        vk is >= NativeMethods.VK_F1 and <= NativeMethods.VK_F24
        || vk == NativeMethods.VK_PAUSE
        || vk == NativeMethods.VK_SCROLL
        || vk is >= NativeMethods.VK_BROWSER_BACK and <= NativeMethods.VK_LAUNCH_APP2;

    /// <summary>Maps WPF's modifier flags onto the MOD_* values RegisterHotKey expects.</summary>
    private static uint ToWin32Modifiers(ModifierKeys modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= NativeMethods.MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= NativeMethods.MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= NativeMethods.MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) result |= NativeMethods.MOD_WIN;
        return result;
    }

    /// <summary>Idempotent — safe to call more than once.</summary>
    public void Dispose() => Unregister();
}
