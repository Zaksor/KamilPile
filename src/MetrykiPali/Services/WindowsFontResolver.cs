using PdfSharp.Fonts;

namespace MetrykiPali.Services;

/// <summary>
/// Hands PDFsharp the Calibri files from the Windows fonts folder. PDFsharp's
/// own Windows lookup knows only a short list of classic fonts and Calibri is
/// not on it. The fonts are embedded in the PDF, so it prints the same on a
/// machine without them. If Calibri is missing, Arial stands in rather than the
/// export failing.
/// </summary>
internal sealed class WindowsFontResolver : IFontResolver
{
    private static readonly string FontsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    // (regular, bold) file names, first one present wins.
    private static readonly (string Regular, string Bold)[] Candidates =
    {
        ("calibri.ttf", "calibrib.ttf"),
        ("arial.ttf", "arialbd.ttf")
    };

    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic)
    {
        foreach (var (regular, boldFile) in Candidates)
        {
            var file = bold ? boldFile : regular;
            if (File.Exists(Path.Combine(FontsFolder, file))) return new FontResolverInfo(file);
        }

        throw new InvalidOperationException("Nie znaleziono czcionki Calibri ani Arial w folderze " + FontsFolder);
    }

    public byte[]? GetFont(string faceName) => File.ReadAllBytes(Path.Combine(FontsFolder, faceName));

    private static readonly object Gate = new();

    /// <summary>Installs the resolver once; PDFsharp allows it to be set only before first use.</summary>
    public static void Install()
    {
        lock (Gate)
        {
            if (GlobalFontSettings.FontResolver is WindowsFontResolver) return;
            GlobalFontSettings.FontResolver = new WindowsFontResolver();
        }
    }
}
