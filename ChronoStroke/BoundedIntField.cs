using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ChronoStroke;

/// <summary>
/// A text box holding a whole number of milliseconds within fixed bounds, together with the last
/// value it held that was inside them.
/// </summary>
/// <remarks>
/// The interval and the step boxes are the same control twice. Before this existed the view model
/// carried six near-identical member pairs to say so — a backing field for the last good value, a
/// bound string, an error and its bool, a changed handler, a validator, and a
/// value-or-last-good accessor — with the two validators differing only in their bounds and the
/// wording of one message. A third numeric setting would have meant a seventh pair.
/// <para>
/// The last-good value is what the spinner counts from. A half-typed or out-of-range box has no
/// usable number in it, so the arrows count from the last one that did rather than doing nothing
/// until the box is fixed by hand.
/// </para>
/// </remarks>
internal sealed partial class BoundedIntField : ObservableObject
{
    private readonly string _belowMinimum;
    private readonly string _aboveMaximum;

    /// <param name="belowMinimum">Shown when the value parses but is under <paramref name="min"/>.</param>
    /// <param name="aboveMaximum">
    /// Shown when it is over <paramref name="max"/>. Pass the same string as
    /// <paramref name="belowMinimum"/> where one message covers both ends.
    /// </param>
    public BoundedIntField(int min, int max, int initial, string belowMinimum, string aboveMaximum)
    {
        Min = min;
        Max = max;
        _belowMinimum = belowMinimum;
        _aboveMaximum = aboveMaximum;

        LastValid = Math.Clamp(initial, min, max);
        Text = Format(LastValid);
    }

    public int Min { get; }

    public int Max { get; }

    /// <summary>The most recent contents that parsed and were in range.</summary>
    public int LastValid { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Error), nameof(HasError))]
    public partial string Text { get; set; }

    /// <summary>Null when the box is usable, otherwise the reason it is not.</summary>
    public string? Error => Validate(Text, out _);

    public bool HasError => Error is not null;

    /// <summary>The box's value if it has a usable one, otherwise <see cref="LastValid"/>.</summary>
    public int ValueOrLastValid => Validate(Text, out var value) is null ? value : LastValid;

    /// <summary>
    /// Raised after <see cref="Text"/> changes and <see cref="LastValid"/> has caught up with it.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>Checks <paramref name="text"/> without touching the box.</summary>
    /// <returns>Null when it is usable, otherwise the reason it is not.</returns>
    public string? Validate(string? text, out int value)
    {
        value = 0;

        // NumberStyles.None is what rejects a leading sign, a decimal point and thousands
        // separators — "-250" and "2.5" are not intervals, and accepting them silently would
        // round or negate what the user typed.
        if (!int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return "Enter a whole number of milliseconds.";
        }

        if (parsed < Min)
        {
            return _belowMinimum;
        }

        if (parsed > Max)
        {
            return _aboveMaximum;
        }

        value = parsed;
        return null;
    }

    /// <summary>
    /// Replaces the contents with a value forced inside the bounds. Used when loading settings,
    /// where the file is hand-editable and a 1 ms interval must not slip past the guard rail the
    /// UI enforces.
    /// </summary>
    public void SetClamped(int value) => Text = Format(Math.Clamp(value, Min, Max));

    /// <summary>
    /// Moves the value by <paramref name="delta"/>, clamped rather than refused, so holding an
    /// arrow down stops at the guard rail instead of walking past it.
    /// </summary>
    public void Nudge(int delta) => SetClamped(ValueOrLastValid + delta);

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

    partial void OnTextChanged(string value)
    {
        // Half-typed values never become the last good one; the previous one stands until the box
        // holds another that is usable.
        if (Validate(value, out var parsed) is null)
        {
            LastValid = parsed;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
