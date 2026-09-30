using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MetrykiPali.Services;

/// <summary>
/// The footer picture. Whatever the user picks (JPG, PNG, BMP, GIF, TIFF) is
/// turned into a PNG no taller than <see cref="MaxHeightPixels"/>, once, when it
/// is chosen - so both writers only ever see one well-formed format, and a
/// photo straight from a phone does not bloat every site file.
/// </summary>
public static class FooterImage
{
    public const int MaxHeightPixels = 300;
    public const long MaxFileBytes = 20 * 1024 * 1024;

    /// <summary>Printed height of the picture on the page, in points (about 1 cm).</summary>
    public const double PrintedHeightPoints = 28;

    public static readonly string[] Extensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };

    /// <summary>The picture as PNG bytes.</summary>
    /// <exception cref="InvalidDataException">Not a picture, or too large a file.</exception>
    public static byte[] Prepare(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxFileBytes)
            throw new InvalidDataException("Plik obrazu jest za duży (ponad 20 MB). Wybierz mniejszy.");

        try
        {
            using var source = Image.FromFile(path);
            var scale = Math.Min(1.0, MaxHeightPixels / (double)source.Height);
            var width = Math.Max(1, (int)Math.Round(source.Width * scale));
            var height = Math.Max(1, (int)Math.Round(source.Height * scale));

            using var bitmap = new Bitmap(width, height);
            bitmap.SetResolution(96, 96);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);
                g.DrawImage(source, 0, 0, width, height);
            }

            using var png = new MemoryStream();
            bitmap.Save(png, ImageFormat.Png);
            return png.ToArray();
        }
        catch (Exception ex) when (ex is OutOfMemoryException or ArgumentException or ExternalException)
        {
            // GDI+ reports "not an image" as OutOfMemoryException.
            throw new InvalidDataException("Tego pliku nie da się odczytać jako obrazu. Wybierz plik JPG, PNG albo BMP.", ex);
        }
    }

    /// <summary>Pixel size of prepared PNG bytes.</summary>
    public static (int Width, int Height) Size(byte[] png)
    {
        using var stream = new MemoryStream(png);
        using var image = Image.FromStream(stream);
        return (image.Width, image.Height);
    }

    /// <summary>Printed size in points: <see cref="PrintedHeightPoints"/> tall, width to keep the proportions.</summary>
    public static (double Width, double Height) PrintedSize(byte[] png)
    {
        var (w, h) = Size(png);
        return (PrintedHeightPoints * w / h, PrintedHeightPoints);
    }
}
