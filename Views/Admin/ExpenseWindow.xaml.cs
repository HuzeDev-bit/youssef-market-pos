using System.Globalization;
using System.Windows;
using System.Windows.Input;
using MarketPos.Data;
using MarketPos.Models;

namespace MarketPos.Views.Admin;

/// <summary>
/// Add or edit an operating expense.
///
/// The category box is editable, so the owner can type a category the list has never seen —
/// a shop's costs do not fit a fixed menu, and being forced into "Other" makes the Money
/// Spent breakdown useless within a month.
/// </summary>
public partial class ExpenseWindow : Window
{
    private readonly Expense? _existing;
    private string? _receiptPath;

    public ExpenseWindow(Expense? existing, Expense? template = null)
    {
        InitializeComponent();
        _existing = existing;

        CategoryBox.ItemsSource = ExpenseRepository.Categories().Select(c => c.Name).ToList();
        MethodBox.ItemsSource = new[] { "Cash", "Bank transfer", "Cheque", "Card", "Other" };
        RepeatBox.ItemsSource = new[] { "Does not repeat", "Weekly", "Monthly", "Yearly" };

        var source = existing ?? template;

        if (existing is not null)
        {
            HeadingText.Text = "Edit expense";
            SubText.Text = "Changing the amount changes the profit figures for that period.";
        }
        else if (template is not null)
        {
            HeadingText.Text = $"{template.Name} — this month";
            SubText.Text = "Copied from last month. Check the amount before saving: bills change.";
            SaveButton.Content = "Add this one";
        }
        else
        {
            HeadingText.Text = "Add expense";
            SubText.Text = "Rent, electricity, water, repairs — anything that is not stock.";
        }

        if (source is not null)
        {
            NameBox.Text = source.Name;
            CategoryBox.Text = source.Category;
            AmountBox.Text = source.Amount.ToString("0.00", CultureInfo.InvariantCulture);
            MethodBox.SelectedItem = source.Method;
            NoteBox.Text = source.Note;
            RepeatBox.SelectedIndex = source.Recurring switch
            {
                Recurrence.Weekly => 1,
                Recurrence.Monthly => 2,
                Recurrence.Yearly => 3,
                _ => 0,
            };
            _receiptPath = existing?.ReceiptPath;
        }
        else
        {
            RepeatBox.SelectedIndex = 0;
        }

        MethodBox.SelectedItem ??= "Cash";
        // A repeated bill belongs to this month, not to the month it was copied from.
        DateBox.SelectedDate = existing?.SpentOn ?? DateTime.Today;
        ShowReceipt();

        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    public static bool AddNew(Window owner) =>
        new ExpenseWindow(null) { Owner = owner }.ShowDialog() == true;

    public static bool Edit(Window owner, Expense expense) =>
        new ExpenseWindow(expense) { Owner = owner }.ShowDialog() == true;

    public static bool Repeat(Window owner, Expense template) =>
        new ExpenseWindow(null, template) { Owner = owner }.ShowDialog() == true;

    private void ShowReceipt() =>
        ReceiptText.Text = string.IsNullOrWhiteSpace(_receiptPath)
            ? "None attached"
            : System.IO.Path.GetFileName(_receiptPath);

    /// <summary>
    /// Stores the path to a photo of the paper receipt rather than a copy of it. The file
    /// stays where the owner put it; the database is a till database, not a photo album.
    /// </summary>
    private void Attach_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a photo of the receipt",
            Filter = "Images and PDF|*.jpg;*.jpeg;*.png;*.pdf|All files|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            _receiptPath = dialog.FileName;
            ShowReceipt();
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            ErrorText.Text = "Say what the money was spent on.";
            NameBox.Focus();
            return;
        }

        if (!decimal.TryParse(AmountBox.Text.Trim().Replace(',', '.'),
                              NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            || amount <= 0m)
        {
            ErrorText.Text = "Enter an amount greater than zero.";
            AmountBox.Focus();
            return;
        }

        var category = CategoryBox.Text.Trim();
        if (category.Length == 0) category = "Other";

        try
        {
            var expense = new Expense
            {
                Id = _existing?.Id ?? 0,
                Name = name,
                CategoryId = ExpenseRepository.AddCategory(category),
                Category = category,
                Amount = amount,
                SpentOn = DateBox.SelectedDate ?? DateTime.Today,
                Method = MethodBox.SelectedItem as string ?? "Cash",
                Note = NoteBox.Text.Trim(),
                ReceiptPath = _receiptPath,
                Recurring = RepeatBox.SelectedIndex switch
                {
                    1 => Recurrence.Weekly,
                    2 => Recurrence.Monthly,
                    3 => Recurrence.Yearly,
                    _ => Recurrence.None,
                },
            };

            if (_existing is null) ExpenseRepository.Create(expense);
            else ExpenseRepository.Update(expense);

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
    }
}
