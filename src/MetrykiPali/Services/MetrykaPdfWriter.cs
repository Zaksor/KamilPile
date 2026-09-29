using System.Globalization;

using MetrykiPali.Model;

using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

using static MetrykiPali.Services.MetrykaWriter;

namespace MetrykiPali.Services;

/// <summary>
/// Writes the metryki straight to PDF, page for page the same as the workbook
/// from <see cref="MetrykaWriter"/> prints: the same 48-row block, the same
/// columns, fonts and borders, scaled onto A4 the way Excel fits the sheet to
/// one page width. The row offsets are the workbook's own, so the two formats
/// cannot drift apart.
///
/// Geometry is worked in sheet points (a 15.75 pt row, Excel's column widths)
/// and scaled once; the numbers were taken from the reference PDF that Excel
/// printed from the workbook ("przyklad/Metryki pali - WYGENEROWANE.pdf").
/// </summary>
public sealed class MetrykaPdfWriter : IMetrykaWriter
{
    // Sheet geometry, in points before scaling. Nominally the rows are 15.75 pt,
    // but Excel lays them out in whole screen pixels; these are the pitches
    // measured on its printout.
    private const double RowHeight = 15.42;
    private const double LabelColumnWidth = 124;     // A
    private const double DataColumnWidth = 50.4;     // B..M

    // What Excel's fit-to-width comes to for 13 columns on A4 with these margins.
    private const double Scale = 0.655;

    // Page margins, points (the workbook's, in inches x 72).
    private const double MarginLeft = 56.7;
    private const double MarginTop = 42.5;
    private const double HeaderTop = 22.7;
    private const double FooterBottom = 22.7;

    private const string FontFamily = "Calibri";

    private static readonly XPen Thin = new(XColors.Black, 0.75);
    private static readonly XPen Medium = new(XColors.Black, 2.0);

    static MetrykaPdfWriter() => WindowsFontResolver.Install();

    public void Write(string path, IReadOnlyList<WorkDay> days, MetrykaSettings settings)
    {
        var pages = Paginate(days, settings.PilesPerPage);
        if (pages.Count == 0)
            throw new InvalidOperationException("Dziennik jest pusty — brak pali do wygenerowania.");

        using var document = new PdfDocument();
        document.Info.Title = "Metryki pali";
        document.Info.Author = settings.Firma;

        for (var i = 0; i < pages.Count; i++)
        {
            var page = document.AddPage();
            page.Size = PageSize.A4;
            page.Orientation = PageOrientation.Portrait;

            using var gfx = XGraphics.FromPdfPage(page);
            WriteHeaderAndFooter(gfx, page, i + 1, settings);

            var state = gfx.Save();
            gfx.TranslateTransform(MarginLeft, MarginTop);
            gfx.ScaleTransform(Scale);
            WriteBlock(gfx, pages[i].Piles, pages[i].Date, settings);
            gfx.Restore(state);
        }

        document.Save(path);
    }

    // ------------------------------------------------------ header / footer

    private static void WriteHeaderAndFooter(XGraphics gfx, PdfPage page, int number, MetrykaSettings s)
    {
        var font = new XFont(FontFamily, 11 * Scale);
        var width = page.Width.Point - 2 * MarginLeft;
        var line = font.GetHeight();

        var header = new XRect(MarginLeft, HeaderTop, width, line);
        gfx.DrawString("BUDOWA: " + StripTrailingDot(s.Budowa), font, XBrushes.Black, header, XStringFormats.TopLeft);
        gfx.DrawString(s.DokumentacjaNaglowek, font, XBrushes.Black, header, XStringFormats.TopRight);

        var footer = new XRect(MarginLeft, page.Height.Point - FooterBottom - line, width, line);
        gfx.DrawString(s.Firma, font, XBrushes.Black, footer, XStringFormats.BottomLeft);
        gfx.DrawString(number.ToString(CultureInfo.InvariantCulture), font, XBrushes.Black, footer, XStringFormats.BottomRight);
    }

    // ---------------------------------------------------------------- block

    private static void WriteBlock(XGraphics gfx, IReadOnlyList<Pile> piles, DateTime date, MetrykaSettings s)
    {
        // Thin rule that separates the printed page header from the body.
        gfx.DrawLine(Thin, Left(1), Top(OffRule), Right(LastDataColumn), Top(OffRule));

        var title = new XFont(FontFamily, 16, XFontStyleEx.Bold);
        gfx.DrawString("METRYKA PALI", title, XBrushes.Black, Cells(OffTitle, 1, OffTitle + 1, LastDataColumn), XStringFormats.Center);

        var text = new XFont(FontFamily, 12);
        FreeText(gfx, "METODA: " + s.Metoda, text, Cells(OffMetoda, 2, OffMetoda + 1, 11));
        FreeText(gfx, "WYKONAWCA: " + s.Wykonawca, text, Cells(OffWykonawca, 2, OffWykonawca + 1, 11));
        FreeText(gfx, "BUDOWA: " + s.Budowa, text, Cells(OffBudowa, 2, OffBudowa + 1, 11));

        gfx.DrawString("DATA:", text, XBrushes.Black, Cells(OffData, 2, OffData, 2), XStringFormats.TopLeft);
        gfx.DrawString(date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), text, XBrushes.Black,
            Cells(OffData, 3, OffData, 5), XStringFormats.TopCenter);

        WriteTable(gfx, piles);

        FreeText(gfx, "UWAGI:", text, Cells(OffUwagi, 2, OffUwagi + 4, 11));
        FreeText(gfx, "KIEROWNIK ROBÓT PALOWYCH:", text, Cells(OffKierownik, 2, OffKierownik + 1, 11));
    }

    private static void WriteTable(XGraphics gfx, IReadOnlyList<Pile> piles)
    {
        var labelFont = new XFont(FontFamily, 12);
        var valueFont = new XFont(FontFamily, 11);
        var dataColumns = LastDataColumn - FirstDataColumn + 1;

        for (var band = 0; band < BandCount; band++)
        {
            var top = OffTable + band * BandHeight;
            var bottom = top + BandHeight - 1;

            Centred(gfx, BandLabels[band], labelFont, Cells(top, 1, bottom, 1));

            for (var i = 0; i < Math.Min(piles.Count, dataColumns); i++)
            {
                var col = FirstDataColumn + i;
                Centred(gfx, Value(band, piles[i]), valueFont, Cells(top, col, bottom, col));
            }
        }

        // Thin grid inside, medium box around the whole table.
        var firstRow = OffTable;
        var lastRow = OffTable + BandCount * BandHeight - 1;

        for (var band = 1; band < BandCount; band++)
        {
            var y = Top(OffTable + band * BandHeight);
            gfx.DrawLine(Thin, Left(1), y, Right(LastDataColumn), y);
        }
        for (var col = FirstDataColumn; col <= LastDataColumn; col++)
            gfx.DrawLine(Thin, Left(col), Top(firstRow), Left(col), Bottom(lastRow));

        gfx.DrawRectangle(Medium, Cells(firstRow, 1, lastRow, LastDataColumn));
    }

    /// <summary>A cell's text as Excel shows it with the General format.</summary>
    private static string Value(int band, Pile pile) => band switch
    {
        0 => Number(pile.Number),
        1 => Number(pile.Diameter),
        2 => Number(pile.DesignLength),
        3 => Number(pile.ActualLength),
        4 => Number(pile.Concrete),
        5 => pile.ConcretePlant,
        _ => pile.Reinforcement
    };

    private static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);

    // ------------------------------------------------------------- geometry

    // Rows are 1-based inside the block, columns 1-based from A, as in the sheet.
    private static double Top(int row) => (row - 1) * RowHeight;
    private static double Bottom(int row) => row * RowHeight;
    private static double Left(int col) => col == 1 ? 0 : LabelColumnWidth + (col - 2) * DataColumnWidth;
    private static double Right(int col) => Left(col) + (col == 1 ? LabelColumnWidth : DataColumnWidth);

    private static XRect Cells(int firstRow, int firstCol, int lastRow, int lastCol)
        => new(Left(firstCol), Top(firstRow), Right(lastCol) - Left(firstCol), Bottom(lastRow) - Top(firstRow));

    // ----------------------------------------------------------------- text

    /// <summary>Wrapped text, centred both ways, as a merged cell with wrap on shows it.</summary>
    private static void Centred(XGraphics gfx, string text, XFont font, XRect cell)
    {
        var lines = Wrap(gfx, text, font, cell.Width - 4);
        var lineHeight = font.GetHeight();
        var y = cell.Top + (cell.Height - lines.Count * lineHeight) / 2;

        foreach (var line in lines)
        {
            gfx.DrawString(line, font, XBrushes.Black, new XRect(cell.Left, y, cell.Width, lineHeight), XStringFormats.TopCenter);
            y += lineHeight;
        }
    }

    /// <summary>Wrapped text from the top-left corner, as the header lines are laid out.</summary>
    private static void FreeText(XGraphics gfx, string text, XFont font, XRect area)
    {
        var lineHeight = font.GetHeight();
        var y = area.Top;

        foreach (var line in Wrap(gfx, text, font, area.Width))
        {
            gfx.DrawString(line, font, XBrushes.Black, new XRect(area.Left, y, area.Width, lineHeight), XStringFormats.TopLeft);
            y += lineHeight;
        }
    }

    private static List<string> Wrap(XGraphics gfx, string text, XFont font, double width)
    {
        var lines = new List<string>();
        var current = "";

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && gfx.MeasureString(candidate, font).Width > width)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0) lines.Add(current);
        return lines;
    }
}
