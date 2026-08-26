using System.Windows.Input;
using ChronoStroke;

namespace ChronoStroke.Tests;

public class KeyComboTests
{
    private const ushort VkF8 = 0x77;
    private const ushort VkNumpad4 = 0x64;

    /// <summary>
    /// Modifier order is fixed so two equal combinations always render identically — the
    /// display name ends up in the settings file's neighbours, in status text and in error
    /// messages, and "Shift+Ctrl+F8" appearing where "Ctrl+Shift+F8" was expected reads as a
    /// different hotkey.
    /// </summary>
    [Fact]
    public void ModifiersRenderInAFixedOrder()
    {
        var all = new KeyCombo(
            VkF8,
            ModifierKeys.Windows | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Control);

        Assert.Equal("Ctrl+Shift+Alt+Win+F8", all.DisplayName);
    }

    [Fact]
    public void SingleModifierRendersWithTheKey()
    {
        Assert.Equal("Ctrl+F8", new KeyCombo(VkF8, ModifierKeys.Control).DisplayName);
        Assert.Equal("F8", new KeyCombo(VkF8, ModifierKeys.None).DisplayName);
    }

    /// <summary>
    /// Numpad keys deliberately skip the character lookup: MAPVK_VK_TO_CHAR maps Numpad 4 to
    /// '4', which would render identically to the top-row 4 while behaving as a different key.
    /// </summary>
    [Fact]
    public void NumpadKeysKeepTheirOwnNames()
    {
        Assert.Equal("NumPad4", new KeyCombo(VkNumpad4, ModifierKeys.None).DisplayName);
    }

    [Fact]
    public void EmptyComboHasNoDisplayName()
    {
        var empty = default(KeyCombo);

        Assert.True(empty.IsEmpty);
        Assert.Equal(string.Empty, empty.DisplayName);
    }

    [Fact]
    public void EqualityIgnoresNothing()
    {
        var ctrlF8 = new KeyCombo(VkF8, ModifierKeys.Control);

        Assert.Equal(ctrlF8, new KeyCombo(VkF8, ModifierKeys.Control));
        Assert.NotEqual(ctrlF8, new KeyCombo(VkF8, ModifierKeys.None));
        Assert.NotEqual(ctrlF8, new KeyCombo(0x78, ModifierKeys.Control));
    }

    /// <summary>
    /// settings.json is plain text in a folder the user can open, so what comes back from it is
    /// not trusted. A key past the end of the virtual-key table cannot be sent at all.
    /// </summary>
    [Theory]
    [InlineData(0x00)]      // unassigned; also how an empty combination is stored
    [InlineData(0xFF)]      // one past VK_OEM_CLEAR, the last entry in the table
    [InlineData(0x1234)]
    [InlineData(ushort.MaxValue)]
    public void AnUnsendableKeyIsDroppedEntirely(int virtualKey)
    {
        var combo = new KeyCombo((ushort)virtualKey, ModifierKeys.Control);

        Assert.True(combo.Sanitised().IsEmpty);
    }

    [Theory]
    [InlineData(0x01)]      // VK_LBUTTON, the first entry
    [InlineData(VkF8)]
    [InlineData(VkNumpad4)]
    [InlineData(0xFE)]      // VK_OEM_CLEAR, the last entry
    public void AKeyInsideTheTableSurvives(int virtualKey)
    {
        var combo = new KeyCombo((ushort)virtualKey, ModifierKeys.Control);

        Assert.Equal(combo, combo.Sanitised());
    }

    /// <summary>
    /// Undefined modifier bits are cleared rather than taken as a reason to drop the combination:
    /// the key is still good, and Ctrl+F8 with a stray bit set is obviously meant to be Ctrl+F8.
    /// </summary>
    [Fact]
    public void UndefinedModifierBitsAreCleared()
    {
        var combo = new KeyCombo(VkF8, ModifierKeys.Control | (ModifierKeys)0x40);

        var clean = combo.Sanitised();

        Assert.Equal(VkF8, clean.VirtualKey);
        Assert.Equal(ModifierKeys.Control, clean.Modifiers);
    }

    [Fact]
    public void EveryDefinedModifierIsKept()
    {
        var all = ModifierKeys.Alt | ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Windows;
        var combo = new KeyCombo(VkF8, all);

        Assert.Equal(all, combo.Sanitised().Modifiers);
    }

    [Fact]
    public void SanitisingIsIdempotent()
    {
        var combo = new KeyCombo(VkF8, ModifierKeys.Control | (ModifierKeys)0x80);

        Assert.Equal(combo.Sanitised(), combo.Sanitised().Sanitised());
    }
}
