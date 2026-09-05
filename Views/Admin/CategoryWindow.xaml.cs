using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using MarketPos.Data;
using MarketPos.Models;
using MarketPos.Services;

namespace MarketPos.Views.Admin;

/// <summary>Add or rename a category, and pick the icon the till shows on its card.</summary>
public partial class CategoryWindow : Window
{
    private readonly CategoryRow? _existing;

    /// <summary>
    /// Where the chosen picture came from, until Save. Nothing is copied into the shop's
    /// folder while the dialog is open — cancelling has to leave no trace on disk.
    /// </summary>
    private string? _pickedFrom;

    /// <summary>The stored file name, cleared when the owner removes the picture.</summary>
    private string _image = string.Empty;

    /// <summary>The kinds of shelf a Moroccan corner shop actually has, one tap away.</summary>
    private static readonly string[] IconChoices =
        ["🥤", "🍞", "🥛", "🍪", "🧴", "🏠", "🥬", "🍎", "🥖", "🧼", "🍗", "🧊", "🛒", "🍚", "☕", "🍬"];

    public CategoryWindow(CategoryRow? existing)
    {
        InitializeComponent();
        _existing = existing;

        Suggestions.ItemsSource = IconChoices;

        if (existing is null)
        {
            HeadingText.Text = "Add category";
            SubText.Text = "Cashiers browse these to find products that have no barcode.";
            IconBox.Text = "🛒";
        }
        else
        {
            HeadingText.Text = "Edit category";
            SubText.Text = $"{existing.ProductCount} product{(existing.ProductCount == 1 ? string.Empty : "s")} in this category.";
            NameBox.Text = existing.Name;
            IconBox.Text = existing.Icon;
            _image = existing.Image;
            DeactivateButton.Visibility = Visibility.Visible;
        }

        ShowPicture();

        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    public static bool AddNew(Window owner) =>
        new CategoryWindow(null) { Owner = owner }.ShowDialog() == true;

    public static bool Edit(Window owner, CategoryRow row) =>
        new CategoryWindow(row) { Owner = owner }.ShowDialog() == true;

    private void Suggestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string icon }) IconBox.Text = icon;
    }

    // ============================== Picture ==============================

    private void Picture_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a picture for this category",
            Filter = "Pictures|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All files|*.*",
            CheckFileExists = true,
        };

        if (picker.ShowDialog(this) != true) return;

        _pickedFrom = picker.FileName;
        ShowPicture();
    }

    private void RemovePicture_Click(object sender, RoutedEventArgs e)
    {
        _pickedFrom = null;
        _image = string.Empty;
        ShowPicture();
    }

    /// <summary>
    /// Draws whichever picture is current: the one just chosen, or the one already stored.
    /// Loaded with OnLoad so the file is not left open — the owner has to be able to replace
    /// a picture without closing the app.
    /// </summary>
    private void ShowPicture()
    {
        var path = _pickedFrom ?? CategoryImages.Find(_image);
        var has = path is not null && File.Exists(path);

        if (has)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(path!);
            bitmap.DecodePixelWidth = 200;
            bitmap.EndInit();
            bitmap.Freeze();
            PictureBox.Source = bitmap;
        }
        else
        {
            PictureBox.Source = null;
        }

        PicturePrompt.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        RemovePicture.Visibility = has ? Visibility.Visible : Visibility.Collapsed;

        PictureNote.Text = has
            ? "Shown on the card instead of the icon. The icon is the fallback."
            : "Optional. Without one the card shows the icon, or the category's initial.";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            ErrorText.Text = "Give the category a name.";
            NameBox.Focus();
            return;
        }

        try
        {
            var icon = OneGlyph(IconBox.Text);

            if (_existing is null)
            {
                // The picture is named after the category, so the row has to exist first.
                var id = CategoryRepository.Create(name, icon);
                if (_pickedFrom is not null)
                    CategoryRepository.Rename(id, name, name, icon, CategoryImageWriter.Save(id, _pickedFrom));
            }
            else
            {
                var image = _pickedFrom is not null
                    ? CategoryImageWriter.Save(_existing.Id, _pickedFrom)
                    : _image;

                // Removing the picture clears the file too, rather than leaving an orphan
                // in the folder that nothing will ever point at again.
                if (image.Length == 0) CategoryImages.Forget(_existing.Id);

                CategoryRepository.Rename(_existing.Id, _existing.Name, name, icon, image);
            }

            DialogResult = true;
            Close();
        }
        catch (Exception error)
        {
            // A duplicate name hits the UNIQUE constraint; say so in shop language.
            ErrorText.Text = error.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
                ? $"There is already a category called {name}."
                : error.Message;
        }
    }

    /// <summary>
    /// Keeps the icon to a single character. The box is free text, so a name typed into it by
    /// mistake used to be stored whole and then spill out of the 42px tile it is drawn in.
    ///
    /// An emoji is picked out of whatever was typed where there is one, rather than blindly
    /// taking the first character — someone who typed a word and then tapped an emoji meant
    /// the emoji, and losing it to salvage the "a" would be the unhelpful reading.
    /// </summary>
    private static string OneGlyph(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return string.Empty;

        string? plain = null;

        var walker = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (walker.MoveNext())
        {
            var glyph = (string)walker.Current;
            var rune = char.ConvertToUtf32(glyph, 0);

            // Anything outside the basic Latin block is the symbol they meant.
            if (rune > 0x24F) return glyph;
            plain ??= glyph;
        }

        return plain ?? string.Empty;
    }

    private void Deactivate_Click(object sender, RoutedEventArgs e)
    {
        if (_existing is null) return;

        if (!ConfirmWindow.Ask(this, $"Deactivate {_existing.Name}?",
                "It stops appearing on the till. Nothing is deleted."))
            return;

        if (!CategoryRepository.SetActive(_existing.Id, _existing.Name, active: false, out var problem))
        {
            ErrorText.Text = problem;
            return;
        }

        DialogResult = true;
        Close();
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
