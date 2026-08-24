using ChronoStroke.Interop;

namespace ChronoStroke.Tests;

/// <summary>
/// Covers which keys may be registered without a modifier. TryRegister itself needs a real HWND
/// and would take the key system-wide for the duration of the test, so the decision is tested
/// through the predicate it delegates to.
/// </summary>
public class GlobalHotKeyTests
{
    /// <summary>
    /// The keys a bare registration would take away from every other application. Space, Enter
    /// and Tab are here specifically because the obvious MAPVK_VK_TO_CHAR test lets all three
    /// through — see IsSafeWithoutModifiers for the measurements.
    /// </summary>
    [Theory]
    [InlineData(0x20, "Space")]
    [InlineData(0x0D, "Enter")]
    [InlineData(0x09, "Tab")]
    [InlineData(0x1B, "Escape")]
    [InlineData(0x08, "Backspace")]
    [InlineData(0x41, "A")]
    [InlineData(0x31, "1")]
    [InlineData(0xBC, "OemComma")]
    [InlineData(0x25, "LeftArrow")]
    [InlineData(0x24, "Home")]
    [InlineData(0x2E, "Delete")]
    [InlineData(0x2D, "Insert")]
    [InlineData(0x60, "NumPad0")]
    [InlineData(0x64, "NumPad4")]
    [InlineData(0x2C, "PrintScreen")]
    [InlineData(0x14, "CapsLock")]
    public void KeysNeededForOrdinaryUseRequireAModifier(ushort vk, string name)
    {
        Assert.False(GlobalHotKey.IsSafeWithoutModifiers(vk), name);
    }

    /// <summary>
    /// The legitimate no-modifier hotkeys. F8 is the one that matters most: the app's own
    /// default is Ctrl+F8, and bare F8 is the obvious thing to reach for when that is taken.
    /// </summary>
    [Theory]
    [InlineData(0x70, "F1")]
    [InlineData(0x77, "F8")]
    [InlineData(0x87, "F24")]
    [InlineData(0x13, "Pause")]
    [InlineData(0x91, "ScrollLock")]
    [InlineData(0xA6, "BrowserBack")]
    [InlineData(0xB3, "MediaPlayPause")]
    [InlineData(0xB7, "LaunchApp2")]
    public void SpareKeysAreAllowedOnTheirOwn(ushort vk, string name)
    {
        Assert.True(GlobalHotKey.IsSafeWithoutModifiers(vk), name);
    }

    /// <summary>
    /// The list fails closed: an unassigned or unrecognised code needs a modifier rather than
    /// being waved through because nothing matched.
    /// </summary>
    [Theory]
    [InlineData(0x00)]
    [InlineData(0x07)] // reserved
    [InlineData(0x88)] // reserved, immediately above F24
    [InlineData(0xFE)] // VK_OEM_CLEAR, top of the table
    public void UnknownKeysRequireAModifier(ushort vk)
    {
        Assert.False(GlobalHotKey.IsSafeWithoutModifiers(vk));
    }

    /// <summary>
    /// F12 is rejected separately by TryRegister as the debugger's, but it sits inside the
    /// function-key range, so this records that the range is deliberately not the whole story.
    /// </summary>
    [Fact]
    public void F12IsInsideTheAllowedRangeAndRejectedElsewhere()
    {
        Assert.True(GlobalHotKey.IsSafeWithoutModifiers(NativeMethods.VK_F12));
    }
}
