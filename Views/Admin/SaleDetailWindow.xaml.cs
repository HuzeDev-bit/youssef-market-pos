using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;
using MarketPos.ViewModels;

namespace MarketPos.Views.Admin;

/// <summary>One receipt line, with the state a refund needs on top of it.</summary>
public sealed class RefundLine : ViewModelBase
{
    private bool _selected;

    public required SaleDetailLine Source { get; init; }
    public event EventHandler? SelectionChanged;

    public string Name => Source.Name;
    public decimal UnitPrice => Source.UnitPrice;
    public decimal LineTotal => Source.LineTotal;

    public string QuantityLabel => Source.Unit == Unit.Kg
        ? $"{Source.Quantity:0.###} kg"
        : $"{Source.Quantity:0.###}";

    public string ReturnedNote => Source.ReturnedQty <= 0m
        ? string.Empty
        : $"{Source.ReturnedQty:0.###} already returned";

    /// <summary>A line that has been fully returned cannot be returned again.</summary>
    public bool CanReturn => Source.Returnable > 0m;

    public bool ShowTick { get; set; }
    public GridLength TickWidth => ShowTick ? new GridLength(34) : new GridLength(0);

    public bool Selected
    {
        get => _selected;
        set
        {
            if (!SetField(ref _selected, value)) return;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The whole outstanding quantity comes back. Part-returning one line is rare
    /// enough in a corner shop that a quantity box per row would cost more than it saves.</summary>
    public decimal ReturnQuantity => Source.Returnable;
    public decimal ReturnValue => Math.Round(ReturnQuantity * Source.UnitPrice, 2);
}

/// <summary>
/// The full receipt, and the two ways to undo it.
///
/// Refund returns some lines and their money; Cancel voids the whole sale. Neither deletes
/// anything — the sale keeps its number, its lines and its time, and gains a status.
/// </summary>
public partial class SaleDetailWindow : Window
{
    private SaleDetail _sale;
    private List<RefundLine> _lines = new();
    private bool _refunding;
    private bool _changed;

    public SaleDetailWindow(int invoiceNumber)
    {
        InitializeComponent();

        _sale = SalesHistoryRepository.Find(invoiceNumber)
                ?? throw new InvalidOperationException($"No sale with receipt number {invoiceNumber}.");

        ReasonBox.ItemsSource = new[]
        {
            "Customer changed their mind", "Wrong item", "Damaged goods",
            "Expired", "Rung up twice", "Price was wrong",
        };

        Bind();
    }

    public static bool Show(Window owner, int invoiceNumber)
    {
        try
        {
            var window = new SaleDetailWindow(invoiceNumber) { Owner = owner };
            window.ShowDialog();
            return window._changed;
        }
        catch (Exception error)
        {
            ConfirmWindow.Ask(owner, "That sale could not be opened", error.Message);
            return false;
        }
    }

    private void Bind()
    {
        Title = $"Receipt #{_sale.InvoiceNumber}";
        HeadingText.Text = $"Receipt #{_sale.InvoiceNumber}";
        SubText.Text = $"{_sale.SoldAt:dddd d MMMM yyyy, HH:mm} · "
                     + $"{(string.IsNullOrWhiteSpace(_sale.CashierName) ? "till" : _sale.CashierName)} · "
                     + $"{_sale.PaymentMethod}";

        StatusText.Text = _sale.Status switch
        {
            SaleStatus.Refunded => "Refunded",
            SaleStatus.PartlyRefunded => "Partly refunded",
            SaleStatus.Cancelled => "Cancelled",
            _ => "Completed",
        };
        StatusBadge.Style = (Style)FindResource(_sale.Status switch
        {
            SaleStatus.Completed => "Badge.Ok",
            SaleStatus.PartlyRefunded => "Badge.Neutral",
            _ => "Badge.Bad",
        });

        _lines = _sale.Lines.Select(l => new RefundLine { Source = l, ShowTick = _refunding }).ToList();
        foreach (var line in _lines) line.SelectionChanged += (_, _) => UpdateRefundTotal();
        Lines.ItemsSource = _lines;

        TickColumn.Width = _refunding ? new GridLength(34) : new GridLength(0);
        BuildTotals();

        var isOpen = _sale.Status is SaleStatus.Completed or SaleStatus.PartlyRefunded;
        var mayRefund = Session.Can(Permission.Refund) && isOpen;
        RefundButton.Visibility = mayRefund && !_refunding ? Visibility.Visible : Visibility.Collapsed;
        CancelSaleButton.Visibility = mayRefund && !_refunding ? Visibility.Visible : Visibility.Collapsed;
        PrintButton.Visibility = _refunding ? Visibility.Collapsed : Visibility.Visible;

        RefundPanel.Visibility = _refunding ? Visibility.Visible : Visibility.Collapsed;
        PrimaryButton.Content = _refunding ? "Confirm refund" : "Close";
        UpdateRefundTotal();
    }

    private void BuildTotals()
    {
        Totals.Children.Clear();

        Add("Subtotal", _sale.Subtotal, muted: true);
        if (_sale.DiscountAmount > 0m) Add("Remise", -_sale.DiscountAmount, muted: true);
        if (_sale.Tax > 0m) Add("Of which VAT", _sale.Tax, muted: true);
        Add("Total", _sale.Total, big: true);

        if (_sale.Refunded > 0m)
        {
            Add("Refunded", -_sale.Refunded, danger: true);
            Add("Net takings", _sale.NetTotal, big: true);
        }

        if (Session.Can(Permission.SeeFinancials))
        {
            Add("Cost of goods", _sale.CostTotal, muted: true);
            Add("Profit", _sale.Profit, accent: _sale.Profit >= 0m, danger: _sale.Profit < 0m);
        }
    }

    private void Add(string label, decimal value, bool muted = false, bool big = false,
                     bool accent = false, bool danger = false)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = label,
            Style = (Style)FindResource(muted ? "Text.CellMuted" : "Text.Cell"),
        };
        if (big) name.FontWeight = FontWeights.SemiBold;

        var amount = new TextBlock
        {
            Text = Loc.Ltr($"{value:N2} DH"),
            Style = (Style)FindResource(big ? "Text.StatSmall" : "Text.Money"),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        if (danger) amount.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Danger");
        else if (accent) amount.Foreground = (System.Windows.Media.Brush)FindResource("Brush.AccentDark");
        else if (muted) amount.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Muted");

        Grid.SetColumn(amount, 1);
        row.Children.Add(name);
        row.Children.Add(amount);
        Totals.Children.Add(row);
    }

    private void UpdateRefundTotal()
    {
        if (!_refunding) return;
        var total = _lines.Where(l => l.Selected).Sum(l => l.ReturnValue);
        RefundTotalText.Text = total <= 0m
            ? "Nothing selected yet."
            : Loc.T("Refunding {0}", Loc.Ltr($"{total:N2} DH"));
    }

    // ------------------------------- Actions -------------------------------

    private void StartRefund_Click(object sender, RoutedEventArgs e)
    {
        _refunding = true;
        ErrorText.Text = string.Empty;
        Bind();
    }

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (!_refunding) { Close(); return; }

        var chosen = _lines.Where(l => l.Selected && l.CanReturn).ToList();
        if (chosen.Count == 0)
        {
            ErrorText.Text = "Tick at least one line to return.";
            return;
        }

        var reason = ReasonBox.Text.Trim();
        if (reason.Length == 0)
        {
            ErrorText.Text = "Say why it is coming back — this goes on the record.";
            ReasonBox.Focus();
            return;
        }

        var total = chosen.Sum(l => l.ReturnValue);
        var restock = RestockBox.IsChecked == true;

        if (!ConfirmWindow.Ask(this, $"Refund {total:N2} DH?",
                restock
                    ? $"{chosen.Count} line{(chosen.Count == 1 ? string.Empty : "s")} go back into stock. "
                      + "The sale stays on record, marked as refunded."
                    : $"{chosen.Count} line{(chosen.Count == 1 ? string.Empty : "s")} are refunded but NOT put "
                      + "back into stock, so the goods count as a loss."))
            return;

        try
        {
            SalesHistoryRepository.Refund(
                _sale.InvoiceNumber,
                chosen.Select(l => (l.Source.Id, l.ReturnQuantity)).ToList(),
                reason, restock);

            _changed = true;
            _refunding = false;
            _sale = SalesHistoryRepository.Find(_sale.InvoiceNumber)!;
            ErrorText.Text = string.Empty;
            Bind();
        }
        catch (Exception error)
        {
            ErrorText.Text = error.Message;
        }
    }

    private void CancelSale_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmWindow.Ask(this, $"Cancel receipt #{_sale.InvoiceNumber}?",
                $"The whole {_sale.Total:N2} DH sale is voided and everything on it goes back into stock. "
                + "The receipt stays on record, marked as cancelled."))
            return;

        try
        {
            SalesHistoryRepository.Cancel(_sale.InvoiceNumber, "Cancelled by " + Session.CurrentName);
            _changed = true;
            _sale = SalesHistoryRepository.Find(_sale.InvoiceNumber)!;
            Bind();
        }
        catch (Exception error)
        {
            ErrorText.Text = error.Message;
        }
    }

    /// <summary>
    /// Reprints the original receipt. Read-only: it does not create a sale, move stock or
    /// touch revenue — and it prints marked as a duplicate so it cannot be passed off as one.
    /// </summary>
    private void Reprint_Click(object sender, RoutedEventArgs e)
    {
        var receipt = SaleRepository.FindByInvoiceNumber(_sale.InvoiceNumber);
        if (receipt is null)
        {
            ErrorText.Text = "That receipt could not be read back.";
            return;
        }

        try
        {
            ReceiptPrinter.PrintSilent(receipt, isDuplicate: true);
            ErrorText.Text = string.Empty;
        }
        catch (Exception error)
        {
            ErrorText.Text = error.Message;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        // Esc backs out of the refund first, so a half-ticked refund is not lost with the window.
        if (_refunding) { _refunding = false; ErrorText.Text = string.Empty; Bind(); }
        else Close();
    }
}
