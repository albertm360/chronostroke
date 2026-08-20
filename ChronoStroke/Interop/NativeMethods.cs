using System.Runtime.InteropServices;

namespace ChronoStroke.Interop;

/// <summary>
/// Raw Win32 declarations. Nothing in here makes decisions — it is a transcription of
/// winuser.h. All the "what should we send" logic lives in <see cref="KeystrokeSender"/>.
/// </summary>
internal static partial class NativeMethods
{
    // ---------------------------------------------------------------- functions

    // LibraryImport is the source-generated form of interop: the generator writes the
    // marshalling stub at compile time instead of the runtime building one via reflection.
    // Every signature here is blittable (no strings, no bools in structs), which is exactly
    // the case LibraryImport is designed for.
    //
    // pInputs is declared 'ref INPUT' rather than 'INPUT[]'. Passing `ref array[0]` gives the
    // native side a pointer to the start of a contiguous block, which is all SendInput wants.
    // Declaring it as an array would make the generator ask for element-count metadata for a
    // round-trip it never needs to do.
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint SendInput(uint cInputs, ref INPUT pInputs, int cbSize);

    // Named with the explicit W suffix so we bind to the Unicode export directly. The
    // encoding-neutral "MapVirtualKey" alias only exists in the C header, not in the DLL.
    [LibraryImport("user32.dll")]
    internal static partial uint MapVirtualKeyW(uint uCode, uint uMapType);

    // RegisterHotKey is the only way to see a keystroke while another window has focus without
    // installing a system-wide keyboard hook. The OS matches the combination itself and posts
    // WM_HOTKEY to our window — we never observe any other key the user types, which is both
    // less invasive and far less likely to upset anti-cheat than a WH_KEYBOARD_LL hook.
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>
    /// Reports the current physical state of a key. Bit 0x8000 of the result means the key is
    /// down right now. (Bit 1 means "pressed since the last call" and is deliberately ignored —
    /// it is per-caller state that would make this non-idempotent.)
    /// </summary>
    [LibraryImport("user32.dll")]
    internal static partial short GetAsyncKeyState(int vKey);

    // ------------------------------------------------------------------ structs

    /// <summary>C: <c>typedef struct tagINPUT { DWORD type; union {...} }</c></summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    /// <summary>
    /// The anonymous union inside INPUT.
    /// <para>
    /// MOUSEINPUT and HARDWAREINPUT are declared even though this app only ever sends keyboard
    /// events, and that is load-bearing: a union is as large as its largest member. MOUSEINPUT
    /// is the largest (32 bytes on x64), so INPUT comes to 40 bytes. If we declared only
    /// KEYBDINPUT the union would be 24 bytes and INPUT would be 32 — and SendInput fails
    /// outright when cbSize is not the size it expects. That failure is silent at compile time.
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    /// <summary>C: <c>WORD wVk; WORD wScan; DWORD dwFlags; DWORD time; ULONG_PTR dwExtraInfo;</c></summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        // ULONG_PTR is pointer-sized: 8 bytes on x64, 4 on x86. Declaring this as uint would
        // compile fine and silently shift every following byte on 64-bit. There is nothing
        // after it in this struct, but it still changes the struct's total size.
        public nuint dwExtraInfo;
    }

    /// <summary>Never populated — present only so <see cref="InputUnion"/> is sized correctly.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    /// <summary>Never populated — present only so <see cref="InputUnion"/> is sized correctly.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    /// <summary>
    /// Computed once rather than hardcoded to 40, so an x86 build stays correct.
    /// </summary>
    internal static readonly int InputSize = Marshal.SizeOf<INPUT>();

    // ---------------------------------------------------------------- constants

    internal const uint INPUT_KEYBOARD = 1;

    internal const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    internal const uint KEYEVENTF_KEYUP = 0x0002;
    internal const uint KEYEVENTF_SCANCODE = 0x0008;

    /// <summary>
    /// Virtual key to scan code, with the extended-key prefix (0xE0 / 0xE1) reported in the
    /// high byte of the result. The plain MAPVK_VK_TO_VSC (0) throws that prefix away.
    /// </summary>
    internal const uint MAPVK_VK_TO_VSC_EX = 4;

    /// <summary>
    /// Virtual key to its unshifted character, for display purposes only. Lets us show "," and
    /// "/" instead of the Key enum's "OemComma" and "OemQuestion". Returns 0 when the key has
    /// no character (function keys, arrows); the top bit flags a dead key.
    /// </summary>
    internal const uint MAPVK_VK_TO_CHAR = 2;

    // Virtual key codes for the modifiers. These are the "either side" variants — Windows
    // resolves them to the left-hand key, which is what a physical Ctrl+X press looks like.
    internal const ushort VK_SHIFT = 0x10;
    internal const ushort VK_CONTROL = 0x11;
    internal const ushort VK_MENU = 0x12; // Alt
    internal const ushort VK_LWIN = 0x5B;
    internal const ushort VK_RWIN = 0x5C;

    /// <summary>Reserved by the debugger at all times — never register it as a hotkey.</summary>
    internal const ushort VK_F12 = 0x7B;

    // ---------------------------------------------------------------- hotkeys

    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const uint MOD_WIN = 0x0008;

    /// <summary>
    /// Suppresses keyboard auto-repeat for the hotkey. Without it, holding the combination for
    /// half a second delivers a stream of WM_HOTKEY messages and the loop toggles on and off
    /// dozens of times before you let go.
    /// </summary>
    internal const uint MOD_NOREPEAT = 0x4000;

    internal const int WM_HOTKEY = 0x0312;

    /// <summary>Applications must use an id in the range 0x0000-0xBFFF.</summary>
    internal const int HotKeyId = 1;

    /// <summary>ERROR_HOTKEY_ALREADY_REGISTERED — another app already owns the combination.</summary>
    internal const int ErrorHotKeyAlreadyRegistered = 1409;
}
