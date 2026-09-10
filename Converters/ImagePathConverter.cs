using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace MarketPos.Converters;

/// <summary>
/// Turns whatever a screen holds for a picture into something it can draw.
///
/// <para>
/// Two kinds of thing arrive here. On the machine that owns the shop it is a path to a file.
/// On a cashier's machine there are no picture files, so it is a token naming the product or
/// category the shop holds a photo for, and the bytes are fetched from the shop once and kept
/// in memory. Both end up as a bitmap loaded fully (BitmapCacheOption.OnLoad) rather than one
/// that leaves WPF holding the source open.
/// </para>
/// </summary>
public sealed class ImagePathConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            if (Services.ShopImages.IsToken(path))
            {
                var bytes = Services.ShopImages.Bytes(path);
                return bytes is null ? null : FromBytes(bytes);
            }

            return File.Exists(path) ? FromFile(path) : null;
        }
        catch
        {
            // A picture that will not decode is a tile without a picture, which every screen
            // here already knows how to draw. It is never worth a dialog in front of a
            // customer -- and a converter that throws does it once per bound item, which is
            // how one unreadable photo became a wall of error boxes.
            return null;
        }
    }

    /// <summary>
    /// A file on this machine.
    ///
    /// IgnoreImageCache matters here: without it WPF keeps the decoded image against the file's
    /// URI, so a photo replaced in the back office goes on showing the old one until the app is
    /// restarted.
    /// </summary>
    private static BitmapImage FromFile(string path)
    {
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

    /// <summary>
    /// Bytes the shop sent.
    ///
    /// Deliberately without IgnoreImageCache. That option makes WPF evict the entry it keeps
    /// against the image's URI when the load finishes -- and an image built from a stream has
    /// no URI, so the eviction looks up a null key and throws "Value cannot be null (Parameter
    /// 'key')". Once per bound picture, which on a page of category cards is once per card.
    /// There is no URI here to cache against, so there is nothing to ignore either.
    /// </summary>
    private static BitmapImage FromBytes(byte[] bytes)
    {
        using var source = new MemoryStream(bytes);

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = source;
        image.DecodePixelWidth = 320;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
