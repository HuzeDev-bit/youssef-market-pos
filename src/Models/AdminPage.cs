namespace MarketPos.Models;

/// <summary>
/// The back-office screens, in sidebar order. AddProduct comes first because it is the only
/// page a cashier can reach, and the sidebar is filtered by what the signed-in person may do.
/// </summary>
public enum AdminPage
{
    AddProduct,
    Dashboard,
    SalesHistory,
    Categories,
    Inventory,
    Suppliers,
    Workers,
    Expenses,
    Reports,
    Activity,
}
