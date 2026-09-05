using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace MarketPos.Services;

/// <summary>
/// Catches barcode scans on a screen that has more than one box to type into.
///
/// A barcode scanner is a keyboard. Where one field holds focus at all times — the till's sale
/// screen — that is harmless. Anywhere else the digits land in whatever the cursor happened to
/// be in: a scan taken while typing a price becomes part of the price, and a scan taken in a
/// product-name box becomes a product literally named "6111234500042".
///
/// Scanners give themselves away by speed. They emit a whole code in a few milliseconds and
/// usually finish with Enter, where the quickest human typing is nearer eighty milliseconds a
/// character. So a run of digits arriving faster than <see cref="BurstGap"/> is taken to be a
/// machine, lifted out of whatever field it was landing in, and handed to whoever is listening.
///
/// The first character is the awkward one: with nothing before it there is no gap to measure.
/// It is allowed through and then taken back once the second character proves it was a scan.
/// That keeps ordinary typing completely untouched — no buffering, no delay, no swallowed
/// keystrokes.
///
/// One implementation, shared. Two copies of timing code like this drift apart the day one of
/// them gains a rule, and the drift is invisible until a real scanner is in someone's hand.
/// </summary>
public sealed class BarcodeScanner
{
    /// <summary>
    /// Anything faster than this between two characters is a machine. Scanners run at one to
    /// fifteen milliseconds; the quickest human typing is nearer eighty.
    /// </summary>
    public static readonly TimeSpan BurstGap = TimeSpan.FromMilliseconds(40);

    /// <summary>Short runs are a person typing a quantity or a price, not a scan.</summary>
    public const int MinimumLength = 6;

    /// <summary>
    /// How long after the last digit a code is taken as finished when no Enter arrives.
    ///
    /// Not every scanner is configured to send one, and a shop that bought its scanner second
    /// hand has no idea which it has. Without this, such a scanner types the code into a field
    /// and nothing ever collects it.
    /// </summary>
    public static readonly TimeSpan QuietAfter = TimeSpan.FromMilliseconds(120);

    private readonly UIElement _host;
    private readonly StringBuilder _digits = new();
    private readonly DispatcherTimer _idle;

    private DateTime _lastKeystroke = DateTime.MinValue;

    /// <summary>The field the opening character landed in, so it can be taken back.</summary>
    private TextBox? _spill;

    public BarcodeScanner(UIElement host)
    {
        _host = host;
        _host.PreviewTextInput += Typed;
        _host.PreviewKeyDown += KeyPressed;

        _idle = new DispatcherTimer { Interval = QuietAfter };
        _idle.Tick += (_, _) => Complete();
    }

    /// <summary>A finished code. Raised on the UI thread.</summary>
    public event EventHandler<string>? Scanned;

    /// <summary>
    /// Answered by the host: false turns the watch off entirely. Used to stand down while a
    /// list is showing rather than a form, or while the caret is already in the barcode box.
    /// </summary>
    public Func<bool>? ShouldWatch { get; set; }

    /// <summary>What a single keystroke means while watching for a scan.</summary>
    public enum Keystroke
    {
        /// <summary>Not part of a barcode. Abandon anything collected so far.</summary>
        NotAScan,

        /// <summary>A digit, but the first one — it could be a person. Let it through for now.</summary>
        PossibleStart,

        /// <summary>A digit inside the burst window. Only a machine types this fast.</summary>
        Burst,
    }

    /// <summary>
    /// The whole rule, kept out of the event handler so it can be exercised directly rather
    /// than by trying to fake keyboard timings.
    ///
    /// Only 0-9: char.IsDigit is also true for Arabic-Indic ٠١٢, and a barcode is never
    /// written in those.
    /// </summary>
    public static Keystroke Classify(string text, TimeSpan sinceLastKeystroke)
    {
        if (text.Length != 1 || text[0] is < '0' or > '9') return Keystroke.NotAScan;
        return sinceLastKeystroke > BurstGap ? Keystroke.PossibleStart : Keystroke.Burst;
    }

    private void Typed(object? sender, TextCompositionEventArgs e)
    {
        if (ShouldWatch is { } watching && !watching()) return;

        var now = DateTime.UtcNow;
        var verdict = Classify(e.Text, now - _lastKeystroke);
        _lastKeystroke = now;

        switch (verdict)
        {
            case Keystroke.NotAScan:
                Reset();
                return;

            case Keystroke.PossibleStart:
                // Could be a person typing a digit, or the opening character of a scan.
                // Nothing can tell them apart yet, so it goes through and is remembered.
                _digits.Clear();
                _digits.Append(e.Text);
                _spill = Keyboard.FocusedElement as TextBox;
                Restart();
                return;

            default:
                // Two characters inside the burst window: a machine. Take back the opening
                // character and swallow the rest.
                TakeBackSpill();
                _digits.Append(e.Text);
                e.Handled = true;
                Restart();
                return;
        }
    }

    /// <summary>A scanner ends with Enter, which is the normal way a code finishes.</summary>
    private void KeyPressed(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Complete()) e.Handled = true;
    }

    /// <summary>Hands over whatever has been collected. True when it was long enough to be a code.</summary>
    private bool Complete()
    {
        _idle.Stop();

        var code = _digits.ToString();
        Reset();

        if (code.Length < MinimumLength) return false;

        Scanned?.Invoke(this, code);
        return true;
    }

    private void Restart()
    {
        _idle.Stop();
        _idle.Start();
    }

    /// <summary>
    /// Removes the character that was let through before the burst was recognised. It sits
    /// immediately before the caret, because that is where the control just inserted it.
    /// </summary>
    private void TakeBackSpill()
    {
        if (_spill is not { } box) return;
        _spill = null;

        var caret = box.CaretIndex;
        if (caret <= 0 || caret > box.Text.Length) return;

        box.Text = box.Text.Remove(caret - 1, 1);
        box.CaretIndex = caret - 1;
    }

    private void Reset()
    {
        _idle.Stop();
        _digits.Clear();
        _spill = null;
        _lastKeystroke = DateTime.MinValue;
    }
}
