namespace MarketPos.ViewModels;

/// <summary>
/// One category on the till's first screen.
///
/// A category is a way of narrowing the grid and nothing else. It is not a thing the shop
/// sells, it has no price, and pressing one must never put anything on the sale — which is
/// why this is its own small type rather than a <see cref="Models.Product"/> that happens to
/// have no price. Nothing here can reach the cart: the command it is bound to sets the filter
/// and returns.
/// </summary>
public sealed class CategoryChoice : ViewModelBase
{
    /// <summary>
    /// The category as the catalogue knows it, and what the filter is set to. Never
    /// translated: "All" is a key the filter compares against, and a shop's own category names
    /// are the shop's words.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// What the box says. Only "All" has anything to translate — it is the app's word, not a
    /// category anybody created — and a real category name comes through untouched.
    /// </summary>
    public string Label => Name == "All" ? Services.Loc.T("All") : Name;

    private bool _isChosen;

    /// <summary>
    /// True for the one the cashier is looking at. Held on the row rather than worked out in
    /// the view, because a themed button cannot compare itself to a property on the window's
    /// view model without a converter, and a converter is a lot of machinery for a bool.
    /// </summary>
    public bool IsChosen
    {
        get => _isChosen;
        set => SetField(ref _isChosen, value);
    }
}
