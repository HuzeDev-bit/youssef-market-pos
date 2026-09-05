using System.IO;
using System.Windows.Media.Imaging;

namespace MarketPos.Services;

/// <summary>
/// Writes a category picture into the shop's folder.
///
/// The other half of <see cref="CategoryImages"/>, kept apart because it is the only part
/// that needs a graphics stack: the chosen file is decoded and re-encoded at card size, so a
/// 4MB photo off a phone is not decoded again on every draw of a 212px tile.
/// </summary>
public static class CategoryImageWriter
{
    /// <summary>
    /// Copies a chosen picture in and returns the file name to store. Named after the category
    /// id so re-picking replaces rather than accumulating, with a stamp appended because the
    /// file name is what tells WPF the image changed.
    /// </summary>
    public static string Save(int categoryId, string sourcePath)
    {
        var source = new BitmapImage();
        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        source.UriSource = new Uri(sourcePath);

        // Decode straight to card size rather than loading the whole photo and shrinking it.
        source.DecodePixelWidth = CategoryImages.MaxEdge;
        source.EndInit();
        source.Freeze();

        var name = $"cat-{categoryId}-{DateTime.UtcNow:yyyyMMddHHmmss}.png";
        var target = Path.Combine(CategoryImages.Folder, name);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using (var file = File.Create(target)) encoder.Save(file);

        CategoryImages.Forget(categoryId, keep: name);
        return name;
    }
}
