using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace MarketPos.Converters;

/// <summary>
/// Loads a product photo fully into memory (BitmapCacheOption.OnLoad) instead of leaving
/// WPF holding the file open. Without this the running app locks every PNG, so photos
/// can't be swapped until the till is closed.
/// </summary>
public sealed class ImagePathConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return null;

        // A till has no picture files. What it has is a token saying which product or category
        // the shop holds a photo for, and the bytes are fetched once and kept in memory.
        if (Services.ShopImages.IsToken(path))
        {
            var bytes = Services.ShopImages.Bytes(path);
            return bytes is null ? null : From(new MemoryStream(bytes));
        }

        return File.Exists(path) ? From(File.OpenRead(path)) : null;
    }

    /// <summary>
    /// Loads fully into memory (BitmapCacheOption.OnLoad) instead of leaving WPF holding the
    /// stream open. Without this the running app locks every PNG, so photos cannot be swapped
    /// until the till is closed.
    /// </summary>
    private static BitmapImage From(Stream source)
    {
        using (source)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.StreamSource = source;
            image.DecodePixelWidth = 320;  // tiles are ~200px; decoding full size wastes memory
            image.EndInit();
            image.Freeze();
            return image;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
