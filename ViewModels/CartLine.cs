using System.Globalization;
using System.Windows.Threading;
using MarketPos.Models;

namespace MarketPos.ViewModels;

/// <summary>One row in the cart: a product plus the quantity being bought.</summary>
public sealed class CartLine : ViewModelBase
{
    public Product Product { get; }

    /// <summary>
    /// The same line with the screen taken off, for saving. The repository takes this rather
    /// than the cart line itself, so writing a sale needs no UI — which is what lets the
    /// server do it too.
    /// </summary>
    public SaleItem AsSaleItem => new(Product, Quantity);

    private decimal _quantity;
    public decimal Quantity
    {
        get => _quantity;
        set
        {
            // Normalise before clamping: weighed goods to the gram, piece goods to whole
            // units (nobody sells 2.7 tins). Rounding here also mops up the drift that
            // repeated 0.1 steps would otherwise accumulate.
            var normalized = Product.Unit == Unit.Kg
                ? Math.Round(value, 3)
                : Math.Round(value, 0, MidpointRounding.AwayFromZero);

            var clamped = Math.Max(MinimumQuantity, normalized);

            if (SetField(ref _quantity, clamped))
            {
                OnPropertyChanged(nameof(LineTotal));
                OnPropertyChanged(nameof(LineTax));
                OnPropertyChanged(nameof(QuantityDisplay));
                OnPropertyChanged(nameof(QuantityText));
            }
        }
    }

    public CartLine(Product product, decimal quantity)
    {
        Product = product;
        _quantity = quantity;
    }

    private bool _isFlashing;

    /// <summary>
    /// True for a moment right after this line is scanned into the cart. The row pulses
    /// green so the cashier gets positive confirmation the scan registered — without it
    /// the only feedback is a quietly changing list, and items get scanned twice.
    /// </summary>
    public bool IsFlashing
    {
        get => _isFlashing;
        private set => SetField(ref _isFlashing, value);
    }

    public void Flash()
    {
        IsFlashing = false;   // restart cleanly if the same line is scanned again
        IsFlashing = true;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            IsFlashing = false;
        };
        timer.Start();
    }

    private decimal MinimumQuantity => Product.Unit == Unit.Kg ? 0.001m : 1m;

    /// <summary>Step size for the +/- buttons: whole units for piece goods, 100 g for weighed goods.</summary>
    public decimal Step => Product.Unit == Unit.Kg ? 0.1m : 1m;

    /// <summary>Unit suffix shown beside the editable quantity box.</summary>
    public string UnitSuffix => Product.Unit == Unit.Kg ? "kg" : "×";

    /// <summary>
    /// The quantity as the cashier types it — bare number, no unit, so the field can be
    /// selected and overwritten in one go. Unparseable input is rejected and the field
    /// snaps back to the last good value.
    /// </summary>
    public string QuantityText
    {
        get => Product.Unit == Unit.Kg
            ? Quantity.ToString("0.###", CultureInfo.InvariantCulture)
            : Quantity.ToString("0", CultureInfo.InvariantCulture);
        set
        {
            if (decimal.TryParse(value?.Replace(',', '.'), NumberStyles.Number,
                                 CultureInfo.InvariantCulture, out var parsed))
            {
                Quantity = parsed;
            }

            // Fired even on a successful parse: "1.3500" and "0" both need to be rewritten
            // in canonical form, and the setter above stays silent when the value is unchanged.
            OnPropertyChanged();
        }
    }

    public string QuantityDisplay => Product.Unit == Unit.Kg
        ? $"{Quantity:0.000} kg"
        : $"{Quantity:0}";

    /// <summary>Tax-inclusive line total (this is what the customer pays for this line).</summary>
    public decimal LineTotal => Math.Round(Product.Price * Quantity, 2);

    /// <summary>VAT portion contained within LineTotal (price is tax-inclusive).</summary>
    public decimal LineTax => Math.Round(LineTotal * Product.TaxRate / (1 + Product.TaxRate), 2);
}
