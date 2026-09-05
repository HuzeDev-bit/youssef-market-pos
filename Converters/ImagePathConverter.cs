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
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        image.UriSource = new Uri(path);
        image.DecodePixelWidth = 320;      // tiles are ~200px; decoding full size wastes memory
        image.EndInit();
        image.Freeze();
        return image;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
