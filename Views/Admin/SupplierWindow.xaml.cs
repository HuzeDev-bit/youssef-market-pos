using System.Windows;
using MarketPos.Models;

namespace MarketPos.Views.Admin;

/// <summary>
/// A supplier's details in a window, for editing one. The form itself is
/// <see cref="SupplierForm"/>; adding a supplier shows that same form on the Suppliers page,
/// in place of the list, rather than in here.
/// </summary>
public partial class SupplierWindow : Window
{
    private readonly SupplierForm _form;

    public SupplierWindow(Supplier? existing)
    {
        InitializeComponent();

        // Capped so a long delivery scrolls inside the form instead of pushing the buttons
        // off the bottom of the screen.
        _form = new SupplierForm(existing) { MaxHeight = 560 };
        _form.Done += (_, saved) =>
        {
            DialogResult = saved;
            Close();
        };
        Card.Child = _form;

        Services.Localizer.Apply(this);
        Services.Responsive.Fit(this);

        // The goods editor needs the width; a contact form on its own does not.
        if (existing is null) Width = 940;
    }

    /// <summary>
    /// Adds a supplier. <paramref name="createdId"/> carries the new row's id, because a
    /// supplier is almost never added in the abstract — somebody is standing there with a
    /// delivery, and the next thing to record is what was in it.
    /// </summary>
    public static bool AddNew(Window owner, out int createdId)
    {
        var window = new SupplierWindow(null).By(owner);
        var saved = window.ShowDialog() == true;
        createdId = saved ? window._form.Created : 0;
        return saved;
    }

    public static bool AddNew(Window owner) => AddNew(owner, out _);

    public static bool Edit(Window owner, Supplier supplier) =>
        new SupplierWindow(supplier).By(owner).ShowDialog() == true;
}
