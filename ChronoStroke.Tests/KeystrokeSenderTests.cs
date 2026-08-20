using System.Windows.Input;
using ChronoStroke;
using ChronoStroke.Interop;

namespace ChronoStroke.Tests;

/// <summary>
/// The flag decisions in <see cref="KeystrokeSender"/>, checked without injecting anything.
/// </summary>
/// <remarks>
/// These are the tests the codebase was already reaching for: BuildKeyEventData is internal
/// rather than private specifically so the extended-key decision can be inspected directly.
/// A wrong flag here does not fail loudly — it sends a different key than the user chose, into
/// whatever window has focus, and the only symptom is that the app "doesn't work in my game".
/// <para>
/// MapVirtualKeyW resolves against the running thread's keyboard layout, so these assert
/// behaviour that holds for the QWERTY-family layouts the scan-code table is shared across.
/// </para>
/// </remarks>
public class KeystrokeSenderTests
{
    private const ushort VkPause = 0x13;
    private const ushort VkNumLock = 0x90;
    private const ushort VkA = 0x41;
    private const ushort VkRightControl = 0xA3;

    /// <summary>
    /// The navigation cluster shares scan codes with the numpad; the extended flag is the only
    /// thing telling them apart. Drop it and Left Arrow becomes Numpad 4.
    /// </summary>
    [Theory]
    [InlineData(0x21)]      // Page Up
    [InlineData(0x22)]      // Page Down
    [InlineData(0x23)]      // End
    [InlineData(0x24)]      // Home
    [InlineData(0x25)]      // Left
    [InlineData(0x26)]      // Up
    [InlineData(0x27)]      // Right
    [InlineData(0x28)]      // Down
    [InlineData(0x2D)]      // Insert
    [InlineData(0x2E)]      // Delete
    public void NavigationKeysAreExtended(ushort virtualKey)
    {
        var ki = KeystrokeSender.BuildKeyEventData(virtualKey, keyUp: false);

        Assert.True(IsScanCodeMode(ki), $"0x{virtualKey:X2} should send a scan code");
        Assert.True(IsExtended(ki), $"0x{virtualKey:X2} must carry KEYEVENTF_EXTENDEDKEY");
    }

    /// <summary>
    /// Regression guard. Num Lock was on the always-extended list on the claim that its scan
    /// code collides with Pause. It does not: Num Lock maps to a bare 0x45 and Pause maps to
    /// 0xE11D, which takes the virtual-key path below. The flag made us send E0 45, which no
    /// Num Lock key ever produces.
    /// </summary>
    [Fact]
    public void NumLockIsNotExtended()
    {
        var ki = KeystrokeSender.BuildKeyEventData(VkNumLock, keyUp: false);

        Assert.True(IsScanCodeMode(ki));
        Assert.Equal(0x45, ki.wScan);
        Assert.False(IsExtended(ki), "Num Lock's scan code is a bare 0x45, not E0 45");
    }

    /// <summary>
    /// Right Ctrl really is extended, and MapVirtualKeyW says so itself via the 0xE0 prefix —
    /// so it must be caught by the prefix branch without appearing on the explicit list.
    /// </summary>
    [Fact]
    public void RightControlIsExtendedFromItsPrefix()
    {
        var ki = KeystrokeSender.BuildKeyEventData(VkRightControl, keyUp: false);

        Assert.True(IsScanCodeMode(ki));
        Assert.True(IsExtended(ki));
    }

    /// <summary>
    /// Pause's real scan sequence is 0xE1 0x1D 0x45 — three bytes that cannot be expressed as
    /// one event. Taking the low byte would send 0x1D, which is Left Ctrl, so the code falls
    /// back to virtual-key mode rather than sending a lie.
    /// </summary>
    [Fact]
    public void PauseFallsBackToVirtualKeyMode()
    {
        var ki = KeystrokeSender.BuildKeyEventData(VkPause, keyUp: false);

        Assert.False(IsScanCodeMode(ki));
        Assert.Equal(VkPause, ki.wVk);
        Assert.Equal(0, ki.wScan);
    }

    [Fact]
    public void OrdinaryKeysSendABareScanCode()
    {
        var ki = KeystrokeSender.BuildKeyEventData(VkA, keyUp: false);

        Assert.True(IsScanCodeMode(ki));
        Assert.NotEqual(0, ki.wScan);
        Assert.False(IsExtended(ki));
        Assert.Equal(0, ki.wVk);        // ignored when KEYEVENTF_SCANCODE is set, so left clear
    }

    [Fact]
    public void KeyUpSetsTheKeyUpFlagAndNothingElseChanges()
    {
        var down = KeystrokeSender.BuildKeyEventData(VkA, keyUp: false);
        var up = KeystrokeSender.BuildKeyEventData(VkA, keyUp: true);

        Assert.Equal(0u, down.dwFlags & NativeMethods.KEYEVENTF_KEYUP);
        Assert.NotEqual(0u, up.dwFlags & NativeMethods.KEYEVENTF_KEYUP);
        Assert.Equal(down.wScan, up.wScan);
        Assert.Equal(down.dwFlags | NativeMethods.KEYEVENTF_KEYUP, up.dwFlags);
    }

    /// <summary>
    /// Press order is modifiers-then-key, the way a physical Ctrl+X happens. Release runs the
    /// same list backwards, so the key comes up before the modifiers do — releasing Ctrl first
    /// would leave a bare X held down for an instant.
    /// </summary>
    [Fact]
    public void ReleaseBatchReversesThePressOrder()
    {
        var combo = new KeyCombo(VkA, ModifierKeys.Control | ModifierKeys.Shift);

        var down = KeystrokeSender.BuildBatch(combo, keyUp: false);
        var up = KeystrokeSender.BuildBatch(combo, keyUp: true);

        Assert.Equal(3, down.Length);            // Ctrl, Shift, A
        Assert.Equal(down.Length, up.Length);

        var downScans = down.Select(i => i.U.ki.wScan).ToArray();
        var upScans = up.Select(i => i.U.ki.wScan).ToArray();
        Assert.Equal(downScans.Reverse(), upScans);

        Assert.All(down, i => Assert.Equal(0u, i.U.ki.dwFlags & NativeMethods.KEYEVENTF_KEYUP));
        Assert.All(up, i => Assert.NotEqual(0u, i.U.ki.dwFlags & NativeMethods.KEYEVENTF_KEYUP));
    }

    [Fact]
    public void EmptyComboProducesNoEventsAndSendsNothing()
    {
        var batch = KeystrokeSender.BuildBatch(default, keyUp: false);

        Assert.Empty(batch);

        // Send must tolerate the empty batch rather than indexing into it.
        var outcome = KeystrokeSender.Send(batch);
        Assert.True(outcome.Success);
    }

    /// <summary>
    /// SendInput rejects any cbSize that is not exactly the size it expects, and does it
    /// silently at runtime. INPUT is 40 bytes on x64 because the union is as large as
    /// MOUSEINPUT, not as large as the KEYBDINPUT this app actually uses.
    /// </summary>
    [Fact]
    public void InputStructIsTheSizeSendInputExpects()
    {
        Assert.Equal(Environment.Is64BitProcess ? 40 : 28, NativeMethods.InputSize);
    }

    private static bool IsScanCodeMode(NativeMethods.KEYBDINPUT ki)
        => (ki.dwFlags & NativeMethods.KEYEVENTF_SCANCODE) != 0;

    private static bool IsExtended(NativeMethods.KEYBDINPUT ki)
        => (ki.dwFlags & NativeMethods.KEYEVENTF_EXTENDEDKEY) != 0;
}
