using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MarketPos.Models;

namespace MarketPos.Services;

/// <summary>
/// Puts the interface into the shop's language as each piece of it is built.
///
/// The alternative was to rewrite every label in every XAML file into a lookup. Four hundred
/// of them, each one a chance to fat-finger a quotation mark in a file the compiler only
/// checks at run time — and the result would be a codebase where you can no longer read a
/// screen by reading its file.
///
/// So the English stays in the XAML, where it doubles as the source text, and each element is
/// translated at the moment it loads. That reaches the rows inside data templates too, which
/// is where a rewrite is easiest to miss.
///
/// One rule decides what gets touched: <b>literal text is furniture, bound text is data</b>. A
/// label typed into the XAML is the app speaking and is translated; anything arriving through a
/// binding is the shop's own — a product called "Pay", a supplier called "Cash" — and is left
/// exactly as the shop typed it.
/// </summary>
public static class Localizer
{
    private static bool _listening;

    /// <summary>
    /// Starts translating everything built from here on. Does nothing at all in English, so
    /// the ordinary case pays no cost — not even the class handler.
    /// </summary>
    public static void Start()
    {
        if (_listening || Loc.Current == Language.English) return;
        _listening = true;

        // Every FrameworkElement in the process announces itself when it loads, including the
        // ones a DataTemplate makes for row nine hundred of a list. Catching them here is what
        // makes this reach further than a rewrite would have.
        EventManager.RegisterClassHandler(
            typeof(FrameworkElement), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => Translate(sender as FrameworkElement)));
    }

    /// <summary>
    /// Translates a whole tree at once.
    ///
    /// The class handler above covers the running app, where every element announces itself
    /// as it loads. Nothing announces itself in a window that is measured and photographed
    /// without ever being shown — which is exactly what the diagnostics do, and why the first
    /// French screenshot came out in English.
    /// </summary>
    public static void Apply(DependencyObject? root)
    {
        if (root is null) return;

        Translate(root as FrameworkElement);

        foreach (var child in Children(root)) Apply(child);
    }

    /// <summary>
    /// Both trees. A ContentPresenter's child is visual only, and a Window's content before
    /// it has been arranged is logical only, so walking either one alone misses labels.
    /// </summary>
    private static IEnumerable<DependencyObject> Children(DependencyObject node)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(node))
            if (child is DependencyObject logical) yield return logical;

        if (node is not System.Windows.Media.Visual and not System.Windows.Media.Media3D.Visual3D) yield break;

        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
            yield return System.Windows.Media.VisualTreeHelper.GetChild(node, i);
    }

    /// <summary>
    /// Translates one element. Safe to run twice: a translated string is not itself a key, so
    /// the second pass finds nothing and changes nothing.
    /// </summary>
    public static void Translate(FrameworkElement? element)
    {
        if (element is null) return;

        switch (element)
        {
            case TextBlock text when IsLiteral(text, TextBlock.TextProperty):
                text.Text = Loc.T(text.Text);
                break;

            // Buttons, checkboxes, radio buttons — anything whose content is a bare string.
            // Content that is a panel of its own is left alone; its children arrive here in
            // their own right.
            case ContentControl holder
                when holder.Content is string content && IsLiteral(holder, ContentControl.ContentProperty):
                holder.Content = Loc.T(content);
                break;

            // A header on a column or an expander, which is content by another name.
            case HeaderedContentControl header
                when header.Header is string title && IsLiteral(header, HeaderedContentControl.HeaderProperty):
                header.Header = Loc.T(title);
                break;
        }

        if (element.ToolTip is string tip && IsLiteral(element, FrameworkElement.ToolTipProperty))
            element.ToolTip = Loc.T(tip);

        LayOut(element);
    }

    /// <summary>
    /// Turns a window over for Arabic: sidebar to the right, columns mirrored, every icon that
    /// means "back" pointing the other way.
    ///
    /// Set on each window as it loads, and deliberately not through
    /// <c>FlowDirectionProperty.OverrideMetadata</c>. That is the tidy way to do it and it
    /// cannot be done here: Window's own static constructor already overrides the metadata for
    /// that property, so a second override throws "PropertyMetadata is already registered" and
    /// takes the app down before its first window exists. FlowDirection inherits, so setting it
    /// on the window reaches everything inside it anyway.
    /// </summary>
    public static void LayOut(FrameworkElement element)
    {
        if (element is not Window window || !Loc.IsRightToLeft) return;
        if (window.FlowDirection == FlowDirection.RightToLeft) return;

        window.FlowDirection = FlowDirection.RightToLeft;
    }

    /// <summary>
    /// True when the value was typed into the XAML rather than arriving from the database.
    ///
    /// This is the whole safety of the approach. Without it a product the shop had named
    /// "Total" would come out as "Totale" on the receipt the customer is handed.
    /// </summary>
    private static bool IsLiteral(DependencyObject element, DependencyProperty property) =>
        BindingOperations.GetBindingExpressionBase(element, property) is null;
}
