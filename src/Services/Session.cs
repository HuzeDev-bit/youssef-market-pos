using MarketPos.Models;

namespace MarketPos.Services;

/// <summary>What a role is allowed to do. One flag per thing worth protecting.</summary>
[Flags]
public enum Permission
{
    None = 0,

    UsePos = 1 << 0,
    SeeOwnSales = 1 << 1,
    SeeAllSales = 1 << 2,
    Discount = 1 << 3,
    Refund = 1 << 4,

    ManageProducts = 1 << 5,
    ManageCategories = 1 << 6,
    ManageInventory = 1 << 7,
    SeeStockMovements = 1 << 8,

    ManageSuppliers = 1 << 9,
    ManagePurchases = 1 << 10,

    ManageWorkers = 1 << 11,
    SeeSalaries = 1 << 12,
    PaySalaries = 1 << 13,

    SeeFinancials = 1 << 14,   // profit, expenses, supplier debt
    ManageExpenses = 1 << 15,
    ManageCash = 1 << 16,
    SeeReports = 1 << 17,
    SeeActivityLog = 1 << 18,
    ManageSettings = 1 << 19,
    ExportData = 1 << 20,

    /// <summary>
    /// Create a product from the till to complete a sale. Deliberately separate from
    /// ManageProducts: a cashier can add the thing in their hand so the customer can pay for
    /// it, without being able to reprice the rest of the shop.
    /// </summary>
    AddProductAtTill = 1 << 21,
}

/// <summary>
/// Who is signed in, and what they may do.
///
/// Permission checks live here rather than in the XAML because hiding a button is a
/// courtesy, not a control: the repositories call <see cref="Require"/> before they write,
/// so a screen reached by any other route still fails closed.
/// </summary>
public static class Session
{
    private static readonly Dictionary<WorkerRole, Permission> Grants = new()
    {
        [WorkerRole.Owner] = (Permission)~0,

        [WorkerRole.Manager] =
            Permission.UsePos | Permission.SeeOwnSales | Permission.SeeAllSales |
            Permission.Discount | Permission.Refund |
            Permission.ManageProducts | Permission.ManageCategories | Permission.ManageInventory |
            Permission.SeeStockMovements | Permission.ManageSuppliers | Permission.ManagePurchases |
            Permission.ManageWorkers | Permission.SeeReports | Permission.ManageCash |
            Permission.ManageExpenses | Permission.ExportData | Permission.SeeActivityLog |
            Permission.AddProductAtTill,
        // Deliberately withheld from Manager: SeeFinancials, SeeSalaries, PaySalaries,
        // ManageSettings. A manager runs the shop floor; profit and payroll are the owner's.

        [WorkerRole.Cashier] =
            Permission.UsePos | Permission.SeeOwnSales | Permission.AddProductAtTill,

        [WorkerRole.StockWorker] =
            Permission.ManageInventory | Permission.SeeStockMovements | Permission.ManageProducts |
            Permission.AddProductAtTill,
    };

    /// <summary>
    /// The signed-in worker. Null means nobody has signed in yet — the till still works,
    /// because a shop must be able to sell when the staff list has not been filled in,
    /// but everything back-office asks for the admin password instead.
    /// </summary>
    public static Worker? Current { get; private set; }

    /// <summary>True once the owner has unlocked the back office with the admin password.</summary>
    public static bool IsOwnerUnlocked { get; private set; }

    public static string CurrentName => Current?.Name ?? (IsOwnerUnlocked ? OwnerLabel : "Till");

    /// <summary>The owner's own name, or the plain word until they have given one.</summary>
    public static string OwnerLabel =>
        string.IsNullOrWhiteSpace(AppSettings.Current.OwnerName)
            ? "Owner" : AppSettings.Current.OwnerName.Trim();
    public static int? CurrentId => Current?.Id;

    /// <summary>
    /// Owner unlock wins over whoever is signed in at the till. Without that, unlocking the
    /// back office while a cashier was signed in would leave every page refusing the owner.
    /// </summary>
    public static WorkerRole CurrentRole =>
        IsOwnerUnlocked ? WorkerRole.Owner : Current?.Role ?? WorkerRole.Cashier;

    public static event EventHandler? Changed;

    public static void SignIn(Worker worker)
    {
        Current = worker;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void SignOut()
    {
        Current = null;
        IsOwnerUnlocked = false;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Called after the admin password check. Grants owner rights for this run of the app
    /// even when no worker record exists yet — otherwise a fresh install could never create
    /// the first worker.
    /// </summary>
    /// <param name="name">
    /// What to call them from here on. Remembered, so it only has to be typed once.
    /// </param>
    public static void UnlockAsOwner(string? name = null)
    {
        if (!string.IsNullOrWhiteSpace(name) && name.Trim() != AppSettings.Current.OwnerName)
        {
            AppSettings.Current.OwnerName = name.Trim();
            AppSettings.Current.Save();
        }

        IsOwnerUnlocked = true;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static bool Can(Permission permission) => Grants[CurrentRole].HasFlag(permission);

    /// <summary>Allows the action if any one of these permissions is held.</summary>
    public static void RequireAny(params Permission[] permissions)
    {
        if (permissions.Any(Can)) return;
        throw new UnauthorizedAccessException(
            $"{CurrentName} ({CurrentRole}) is not allowed to {Describe(permissions[0])}.");
    }

    /// <summary>Throws when the current user may not do this. Repositories call it before writing.</summary>
    public static void Require(Permission permission)
    {
        if (Can(permission)) return;
        throw new UnauthorizedAccessException(
            $"{CurrentName} ({CurrentRole}) is not allowed to {Describe(permission)}.");
    }

    private static string Describe(Permission permission) => permission switch
    {
        Permission.SeeFinancials => "view business financials",
        Permission.SeeSalaries => "view worker salaries",
        Permission.PaySalaries => "record salary payments",
        Permission.ManageSettings => "change business settings",
        Permission.Refund => "refund a sale",
        Permission.Discount => "apply a discount",
        _ => permission.ToString(),
    };
}
