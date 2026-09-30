using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MetrykiPali.Services;

/// <summary>
/// The footer picture. Whatever the user picks (JPG, PNG, BMP, GIF, TIFF) is
/// turned into a PNG once, when it is chosen, so both writers only ever see one
/// well-formed format. It keeps its resolution (dpi), because that is what says
/// how big it was designed to print: a company footer made for A4 at 300 dpi is
/// 2480 px wide - the full width of the page.
/// </summary>
public static class FooterImage
{
    /// <summary>Longest side kept; beyond this a photo only bloats the site file.</summary>
    public const int MaxPixels = 3000;
    public const long MaxFileBytes = 20 * 1024 * 1024;

    /// <summary>The printable width of the A4 page between the side margins, points (about 17 cm).</summary>
    public const double MaxPrintedWidthPoints = 595.28 - 2 * 56.7;

    /// <summary>The tallest the picture prints, points (about 3 cm) - it must stay a footer.</summary>
    public const double MaxPrintedHeightPoints = 85;

    /// <summary>Resolution assumed for a picture that does not state one.</summary>
    private const double DefaultDpi = 96;

    public static readonly string[] Extensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };

    /// <summary>The picture as PNG bytes, with its resolution kept.</summary>
    /// <exception cref="InvalidDataException">Not a picture, or too large a file.</exception>
    public static byte[] Prepare(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxFileBytes)
            throw new InvalidDataException("Plik obrazu jest za duży (ponad 20 MB). Wybierz mniejszy.");

        try
        {
            using var source = Image.FromFile(path);
            var scale = Math.Min(1.0, MaxPixels / (double)Math.Max(source.Width, source.Height));
            var width = Math.Max(1, (int)Math.Round(source.Width * scale));
            var height = Math.Max(1, (int)Math.Round(source.Height * scale));

            // Scaling down in pixels scales the resolution with it, so the
            // printed size stays what the picture was made for.
            var dpiX = Dpi(source.HorizontalResolution) * scale;
            var dpiY = Dpi(source.VerticalResolution) * scale;

            using var bitmap = new Bitmap(width, height);
            bitmap.SetResolution((float)dpiX, (float)dpiY);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);
                g.DrawImage(source, new Rectangle(0, 0, width, height));
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

    private static double Dpi(float stated) => stated is > 1 and < 5000 ? stated : DefaultDpi;

    /// <summary>Pixel size of prepared PNG bytes.</summary>
    public static (int Width, int Height) Size(byte[] png)
    {
        using var stream = new MemoryStream(png);
        using var image = Image.FromStream(stream);
        return (image.Width, image.Height);
    }

    /// <summary>
    /// Printed size in points: the size the picture was designed at (pixels
    /// over its dpi), made smaller if needed to fit between the side margins
    /// and within <see cref="MaxPrintedHeightPoints"/>, proportions kept.
    /// </summary>
    public static (double Width, double Height) PrintedSize(byte[] png)
    {
        using var stream = new MemoryStream(png);
        using var image = Image.FromStream(stream);
        var width = image.Width / Dpi(image.HorizontalResolution) * 72;
        var height = image.Height / Dpi(image.VerticalResolution) * 72;

        var scale = Math.Min(1.0, Math.Min(MaxPrintedWidthPoints / width, MaxPrintedHeightPoints / height));
        return (width * scale, height * scale);
    }

    /// <summary>
    /// A picture wider than half the page - a whole company footer rather than a
    /// logo. It takes the middle of the footer, with the text and page number on
    /// a line above it, since beside it they would overlap it.
    /// </summary>
    public static bool IsWide(byte[] png) => PrintedSize(png).Width > MaxPrintedWidthPoints / 2;
}
