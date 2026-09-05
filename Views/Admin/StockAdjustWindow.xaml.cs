using System.Globalization;
using System.Windows;
using System.Windows.Input;
using MarketPos.Data;
using MarketPos.Models;

namespace MarketPos.Views.Admin;

/// <summary>
/// Changes one product's stock, with a reason attached.
///
/// Two modes, because a shop thinks in two ways. "Add or remove" is a delivery or a breakage;
/// "set counted total" is a shelf count, where the person knows what is there and not what
/// changed. The second is converted into a movement of the difference, so the history stays
/// a continuous chain rather than a series of overwrites.
/// </summary>
public partial class StockAdjustWindow : Window
{
    private readonly StockItem _item;

    private static readonly (string Label, StockReason Reason)[] Reasons =
    [
        ("Supplier purchase", StockReason.SupplierPurchase),
        ("Customer return", StockReason.CustomerReturn),
        ("Damaged", StockReason.Damaged),
        ("Expired", StockReason.Expired),
        ("Lost", StockReason.Lost),
        ("Stolen", StockReason.Stolen),
        ("Used in the shop", StockReason.InternalUse),
        ("Returned to supplier", StockReason.SupplierReturn),
        ("Manual correction", StockReason.ManualCorrection),
    ];

    public StockAdjustWindow(StockItem item)
    {
        InitializeComponent();
        _item = item;

        HeadingText.Text = item.Name;
        SubText.Text = $"{item.Stock:0.###} in stock · {item.Barcode}";
        Title = $"Adjust {item.Name}";

        ReasonBox.ItemsSource = Reasons.Select(r => r.Label).ToList();
        ReasonBox.SelectedIndex = 0;
        UpdatePreview();

        Loaded += (_, _) => { QuantityBox.Focus(); QuantityBox.SelectAll(); };
    }

    public static bool Show(Window owner, StockItem item) =>
        new StockAdjustWindow(item) { Owner = owner }.ShowDialog() == true;

    private bool IsCount => ModeCount.IsChecked == true;

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (QuantityLabel is null) return;

        QuantityLabel.Text = IsCount ? "COUNTED TOTAL" : "QUANTITY";
        ReasonBox.IsEnabled = !IsCount;
        if (IsCount) ReasonBox.SelectedIndex = Array.FindIndex(Reasons, r => r.Reason == StockReason.ManualCorrection);
        UpdatePreview();
    }

    private void Quantity_Changed(object sender, RoutedEventArgs e) => UpdatePreview();

    /// <summary>Spells out the resulting count before it is saved, so a typo is caught by eye.</summary>
    private void UpdatePreview()
    {
        if (PreviewText is null) return;

        if (!TryQuantity(out var typed))
        {
            PreviewText.Text = "Enter a quantity.";
            ValueText.Text = string.Empty;
            return;
        }

        var reason = Reasons[Math.Max(0, ReasonBox.SelectedIndex)].Reason;
        var delta = IsCount ? typed - _item.Stock : Signed(typed, reason);
        var after = _item.Stock + delta;

        PreviewText.Text = delta == 0m
            ? $"No change — {_item.Name} stays at {_item.Stock:0.###}."
            : $"{_item.Name}: {_item.Stock:0.###} → {after:0.###} ({(delta > 0 ? "+" : "−")}{Math.Abs(delta):0.###}).";

        ValueText.Text = _item.Cost > 0m && delta != 0m
            ? $"{(delta < 0 ? "Value removed" : "Value added")}: {Math.Abs(delta) * _item.Cost:N2} DH at cost."
            : string.Empty;

        PreviewText.Foreground = (System.Windows.Media.Brush)FindResource(
            after < 0m ? "Brush.Danger" : "Brush.Text");
    }

    /// <summary>
    /// The owner types a positive number; the reason decides the direction. Asking someone to
    /// type "−3" for a broken bottle is how minus signs get forgotten.
    /// </summary>
    private static decimal Signed(decimal quantity, StockReason reason) => reason switch
    {
        StockReason.SupplierPurchase or StockReason.CustomerReturn => quantity,
        StockReason.ManualCorrection => quantity,
        _ => -quantity,
    };

    private bool TryQuantity(out decimal value) =>
        decimal.TryParse(QuantityBox.Text.Trim().Replace(',', '.'),
                         NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryQuantity(out var typed))
        {
            ErrorText.Text = "Enter a quantity, like 12 or 2.5.";
            QuantityBox.Focus();
            return;
        }

        try
        {
            if (IsCount)
            {
                if (typed < 0m) { ErrorText.Text = "A counted total cannot be negative."; return; }
                InventoryRepository.SetCount(_item.Id, _item.Name, typed, NoteBox.Text.Trim());
            }
            else
            {
                var reason = Reasons[Math.Max(0, ReasonBox.SelectedIndex)].Reason;
                var delta = Signed(typed, reason);
                if (delta == 0m) { ErrorText.Text = "Enter a quantity greater than zero."; return; }

                if (_item.Stock + delta < 0m)
                {
                    ErrorText.Text = $"That would take {_item.Name} to {_item.Stock + delta:0.###}. "
                                   + $"There are only {_item.Stock:0.###} in stock.";
                    return;
                }

                InventoryRepository.Move(_item.Id, delta, reason, reference: "Manual",
                                         note: NoteBox.Text.Trim());
                ActivityRepository.Record("changed stock", "Product", _item.Id,
                    oldValue: _item.Stock.ToString("0.###"),
                    newValue: (_item.Stock + delta).ToString("0.###"),
                    detail: $"changed {_item.Name} stock");
            }

            DialogResult = true;
            Close();
        }
        catch (Exception error)
        {
            ErrorText.Text = error.Message;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Cancel_Click(sender, e);
        else if (e.Key == Key.Enter) Save_Click(sender, e);
    }
}
