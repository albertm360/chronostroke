using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace ChronoStroke.Interop;

/// <summary>
/// Owns one system-wide hotkey registration for a window.
/// </summary>
/// <remarks>
/// The window handle must outlive this object — WM_HOTKEY is posted to that window's message
/// queue, and unregistering after the HWND is destroyed is pointless. In practice the window
/// disposes this during OnClosing, while its handle is still valid.
/// </remarks>
internal sealed class GlobalHotKey(IntPtr windowHandle) : IDisposable
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
