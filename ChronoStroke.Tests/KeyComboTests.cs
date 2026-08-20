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
}
