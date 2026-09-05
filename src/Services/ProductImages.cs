using System.IO;

namespace MarketPos.Services;

/// <summary>
/// Finds a product photo on disk by barcode, so adding artwork is a file-copy job and
/// never a code change. Drop "6111234500042.png" into the images folder and Milk 1L
/// picks it up on next start.
///
/// Lives beside the database in %AppData%\MarketPos\Images rather than next to the exe,
/// so a rebuild or reinstall can't wipe the client's photos.
/// </summary>
public static class ProductImages
{
    private static readonly string[] Extensions = { ".png", ".webp", ".jpg", ".jpeg" };

    public static string Folder { get; } = BuildFolder();

    private static string BuildFolder()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MarketPos", "Images");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Full path to this product's photo, or null to fall back to the placeholder glyph.</summary>
    public static string? Find(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode)) return null;

        foreach (var extension in Extensions)
        {
            var candidate = Path.Combine(Folder, barcode + extension);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>
    /// Removes this product's photo. Every extension, not just the one that was written: a
    /// shop that dropped in a .jpg by hand and then picked a .png through the app would
    /// otherwise still be showing the .jpg afterwards.
    /// </summary>
    public static void Forget(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode)) return;

        foreach (var extension in Extensions)
        {
            var path = Path.Combine(Folder, barcode + extension);
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { /* still open somewhere; it will be replaced next time */ }
        }
    }

    /// <summary>
    /// True when this path is simply where a photo for that barcode lives.
    ///
    /// The catalogue falls back to finding a file by barcode when the database holds no path,
    /// and the found path was then being written back on the next save — baking a machine's
    /// own folder into a row that a second till would later read. This is how a save tells
    /// "somebody typed a path" apart from "we found the usual file".
    /// </summary>
    public static bool IsTheUsualPlace(string? path, string barcode) =>
        !string.IsNullOrWhiteSpace(path)
        && string.Equals(Path.GetDirectoryName(path), Folder, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Path.GetFileNameWithoutExtension(path), barcode, StringComparison.OrdinalIgnoreCase);
}
