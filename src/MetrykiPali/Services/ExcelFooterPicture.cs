using System.Globalization;
using System.Text;

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

using MetrykiPali.Model;

namespace MetrykiPali.Services;

/// <summary>
/// Puts a picture into the printed footer of a workbook ClosedXML has written.
///
/// ClosedXML has no header/footer pictures, so this adds them the way Excel
/// itself stores one ("Układ strony → Nagłówek/stopka → Obraz"): a VML drawing
/// part holding a shape named after the footer section (LF / CF / RF) that
/// points at the image, referenced from the sheet by legacyDrawingHF, and
/// "&amp;G" in that section of the footer text.
/// </summary>
internal static class ExcelFooterPicture
{
    /// <summary>
    /// Excel scales header and footer with the page's fit-to-width. For the
    /// footer picture that came to 0.70 (measured on Excel's own PDF export),
    /// so the picture is drawn that much larger to print at
    /// <see cref="FooterImage.PrintedHeightPoints"/>, the size the PDF uses.
    /// </summary>
    private const double PrintScale = 0.70;

    public static void Add(string path, MetrykaSettings settings)
    {
        if (settings.FooterImage is not { Length: > 0 } png) return;

        using var document = SpreadsheetDocument.Open(path, isEditable: true);
        var sheetPart = document.WorkbookPart!.WorksheetParts.First();
        var worksheet = sheetPart.Worksheet;

        var vml = sheetPart.AddNewPart<VmlDrawingPart>();
        var image = vml.AddImagePart(ImagePartType.Png);
        using (var data = new MemoryStream(png)) image.FeedData(data);

        var (width, height) = FooterImage.PrintedSize(png);
        var shapeId = settings.FooterImagePosition switch
        {
            FooterPosition.Left => "LF",
            FooterPosition.Right => "RF",
            _ => "CF"
        };
        using (var stream = vml.GetStream(FileMode.Create))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            writer.Write(Vml(shapeId, vml.GetIdOfPart(image), width / PrintScale, height / PrintScale));

        // The footer text, with the picture's section holding "&G".
        var footer = worksheet.GetFirstChild<HeaderFooter>() ?? worksheet.AppendChild(new HeaderFooter());
        footer.OddFooter = new OddFooter(FooterText(settings));

        var reference = new LegacyDrawingHeaderFooter { Id = sheetPart.GetIdOfPart(vml) };
        var after = worksheet.ChildElements.FirstOrDefault(e =>
            e is Picture or OleObjects or Controls or WebPublishItems or TableParts or WorksheetExtensionList);
        if (after is null) worksheet.AppendChild(reference);
        else worksheet.InsertBefore(reference, after);

        worksheet.Save();
    }

    /// <summary>
    /// Where the three footer items go. The text sits left and the page number
    /// right; the picture takes its chosen section and moves whichever of those
    /// was there to the middle.
    /// </summary>
    public static (string Left, string Center, string Right) Sections(MetrykaSettings s, string picture, string text, string pageNumber)
        => s.FooterImagePosition switch
        {
            FooterPosition.Left => (picture, text, pageNumber),
            FooterPosition.Right => (text, pageNumber, picture),
            _ => (text, picture, pageNumber)
        };

    private static string FooterText(MetrykaSettings s)
    {
        var (left, center, right) = Sections(s, "&G", Escape(s.Firma), "&P");
        return $"&L{left}&C{center}&R{right}";
    }

    /// <summary>"&amp;" starts a code in Excel's header text; a literal one is doubled.</summary>
    private static string Escape(string text) => text.Replace("&", "&&");

    private static string Vml(string shapeId, string imageRelId, double widthPt, double heightPt)
    {
        var w = widthPt.ToString("0.##", CultureInfo.InvariantCulture);
        var h = heightPt.ToString("0.##", CultureInfo.InvariantCulture);
        return
            "<xml xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns:x=\"urn:schemas-microsoft-com:office:excel\">" +
            "<o:shapelayout v:ext=\"edit\"><o:idmap v:ext=\"edit\" data=\"1\"/></o:shapelayout>" +
            "<v:shapetype id=\"_x0000_t75\" coordsize=\"21600,21600\" o:spt=\"75\" o:preferrelative=\"t\" path=\"m@4@5l@4@11@9@11@9@5xe\" filled=\"f\" stroked=\"f\">" +
            "<v:stroke joinstyle=\"miter\"/><v:formulas>" +
            "<v:f eqn=\"if lineDrawn pixelLineWidth 0\"/><v:f eqn=\"sum @0 1 0\"/><v:f eqn=\"sum 0 0 @1\"/>" +
            "<v:f eqn=\"prod @2 1 2\"/><v:f eqn=\"prod @3 21600 pixelWidth\"/><v:f eqn=\"prod @3 21600 pixelHeight\"/>" +
            "<v:f eqn=\"sum @0 0 1\"/><v:f eqn=\"prod @6 1 2\"/><v:f eqn=\"prod @7 21600 pixelWidth\"/>" +
            "<v:f eqn=\"sum @8 21600 0\"/><v:f eqn=\"prod @7 21600 pixelHeight\"/><v:f eqn=\"sum @10 21600 0\"/>" +
            "</v:formulas><v:path o:extrusionok=\"f\" gradientshapeok=\"t\" o:connecttype=\"rect\"/>" +
            "<o:lock v:ext=\"edit\" aspectratio=\"t\"/></v:shapetype>" +
            $"<v:shape id=\"{shapeId}\" o:spid=\"_x0000_s1025\" type=\"#_x0000_t75\" style=\"position:absolute;margin-left:0;margin-top:0;width:{w}pt;height:{h}pt;z-index:1\">" +
            $"<v:imagedata o:relid=\"{imageRelId}\" o:title=\"stopka\"/><o:lock v:ext=\"edit\" rotation=\"t\"/></v:shape>" +
            "</xml>";
    }
}
