using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ChronoStroke;

/// <summary>
/// A read-only text box that records the next key combination pressed into it.
/// </summary>
/// <remarks>
/// There is deliberately no way to clear a captured combination, which the review raised as
/// either a gap to fill or a decision to write down. It is the second. A combination is replaced
/// by pressing another one, and emptying a box is not a state anything wants: Start requires a
/// key to send <em>and</em> a registered hotkey, so a cleared box only produces an app that
/// cannot run. An affordance whose single outcome is to disable the thing the window is for is
/// worse than its absence.
/// <para>
/// The empty state itself is real and reachable — it is what a key code the loader rejected
/// leaves behind, so it reads "(not set)" with Start unavailable until a key is pressed. That is
/// a recovery path rather than a choice, which is exactly why it needs no button.
/// </para>
/// </remarks>
internal sealed class KeyCaptureBox : TextBox
{
    static KeyCaptureBox()
    {
        // Fallback for the case where no implicit TextBox style is in scope.
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(KeyCaptureBox),
            new FrameworkPropertyMetadata(typeof(TextBox)));
    }

    public static readonly DependencyProperty ComboProperty = DependencyProperty.Register(
        nameof(Combo),
        typeof(KeyCombo),
        typeof(KeyCaptureBox),
        new FrameworkPropertyMetadata(
            default(KeyCombo),
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnComboChanged));

    /// <summary>The captured combination. Binds two-way by default.</summary>
    public KeyCombo Combo
    {
        get => (KeyCombo)GetValue(ComboProperty);
        set => SetValue(ComboProperty, value);
    }

    public KeyCaptureBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        ContextMenu = null;         // cut/paste makes no sense on a key field
        Cursor = Cursors.Hand;

        // Adopt the Fluent theme's TextBox appearance.
        //
        // DefaultStyleKey alone is not enough: it governs lookup in *theme* dictionaries
        // (generic.xaml), but Fluent.xaml supplies its TextBox style as an *implicit* style in
        // Application.Resources, which WPF matches on the element's exact runtime type. A class
        // derived from TextBox therefore matches nothing and renders in the stock white Aero2
        // style — visibly wrong next to its siblings in dark mode.
        //
        // Implicit styles are stored under the type as the resource key, so asking for that key
        // by name fetches the very style an ordinary TextBox would have received. Using a
        // resource *reference* rather than a fixed value means it re-resolves if the theme
        // changes underneath us.
        SetResourceReference(StyleProperty, typeof(TextBox));

        UpdateText();
    }

    private static void OnComboChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((KeyCaptureBox)d).UpdateText();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Swallow everything while focused. Without this, Tab moves focus, Space and Enter
        // activate buttons, and none of those keys could ever be captured.
        e.Handled = true;

        // Marked handled first, then passed on: an external handler watching this box should see
        // the key and see that it has been claimed. Skipping base suppresses the routed event
        // entirely, which is a different thing from capturing it.
        base.OnPreviewKeyDown(e);

        // WPF reports Key.System for any key pressed while Alt is held — the actual key is
        // parked in SystemKey. Reading e.Key alone would capture "System" for every Alt combo.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Swallowing Tab along with everything else means a keyboard-only user who tabs into
        // this box can never tab out of it — no Start button, no other field, no way back
        // without a mouse. One key has to stay reserved as the exit. Escape is the conventional
        // choice and costs the least: Windows and most games claim it, so it makes a poor
        // hotkey and a poor key to repeat. Held with a modifier it is still capturable.
        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            return;
        }

        // Ignore a modifier pressed on its own, so building up Ctrl+Shift+E by holding Ctrl,
        // then Shift, then tapping E captures the whole thing rather than committing on Ctrl.
        if (IsModifierKey(key))
        {
            return;
        }

        var vk = (ushort)KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0)
        {
            // Keys consumed by the IME or a dead-key sequence have no virtual key to record.
            return;
        }

        Combo = new KeyCombo(vk, Keyboard.Modifiers);
    }

    private static bool IsModifierKey(Key key) => key
        is Key.LeftCtrl or Key.RightCtrl
        or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt
        or Key.LWin or Key.RWin;

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        UpdateText();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        UpdateText();
    }

    private void UpdateText()
    {
        Text = Combo.IsEmpty
            ? (IsKeyboardFocusWithin ? "Press a key\u2026" : "(not set)")
            : Combo.DisplayName;
    }
}
