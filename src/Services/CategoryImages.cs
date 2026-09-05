using System.IO;

namespace MarketPos.Services;

/// <summary>
/// Pictures for the category cards.
///
/// Kept beside the product photos in %AppData%\MarketPos\Images\Categories, so a rebuild or
/// a reinstall cannot wipe them. What is stored against the category is the file name, never
/// a full path: the owner picks a photo from wherever it happens to live — a phone dump, a
/// USB stick — and a path into that folder would break the moment the stick came out.
///
/// Finding and forgetting a picture is plain file work and lives here, where the server and
/// the till can both do it. Writing one has to decode and re-encode the image, which needs a
/// graphics stack, so that half lives with the app that has one.
/// </summary>
public static class CategoryImages
{
    /// <summary>Longest edge kept. Cards draw at 212px; twice that covers a high-DPI screen.</summary>
    public const int MaxEdge = 424;

    public static string Folder { get; } = BuildFolder();

    private static string BuildFolder()
    {
        var dir = Path.Combine(ProductImages.Folder, "Categories");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Full path to a stored picture, or null when the name is empty or gone.</summary>
    public static string? Find(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var path = Path.Combine(Folder, fileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Drops this category's other pictures. Called after a save so replacing a picture ten
    /// times leaves one file, not ten — and on removal, where nothing is kept.
    /// </summary>
    public static void Forget(int categoryId, string? keep = null)
    {
        foreach (var file in Directory.EnumerateFiles(Folder, $"cat-{categoryId}-*.png"))
        {
            if (keep is not null && Path.GetFileName(file) == keep) continue;
            try { File.Delete(file); }
            catch (IOException) { /* still open somewhere; it will be swept next time */ }
        }
    }
}
