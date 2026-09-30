using ClosedXML.Excel;

using MetrykiPali.Model;

namespace MetrykiPali.Services;

/// <summary>
/// Writes the "METRYKA PALI" workbook. The layout mirrors the reference
/// documentation: one page per 12 piles, laid out on a repeating 48-row block
/// so that page breaks fall exactly where they do in the original.
/// </summary>
public sealed class MetrykaWriter : IMetrykaWriter
{
    // The page layout, shared with MetrykaPdfWriter so both formats print alike.
    internal const int RowsPerBlock = 48;
    internal const int FirstDataColumn = 2;   // B
    internal const int LastDataColumn = 13;   // M

    // Row offsets inside a block, relative to the block base row.
    internal const int OffRule = 1;        // 1..2   thin rule under the page header
    internal const int OffTitle = 3;       // 3..4   "METRYKA PALI"
    internal const int OffMetoda = 6;      // 6..7
    internal const int OffWykonawca = 8;   // 8..9
    internal const int OffBudowa = 10;     // 10..11
    internal const int OffData = 12;       // 12
    internal const int OffTable = 15;      // 15..35, seven 3-row bands
    internal const int OffUwagi = 39;      // 39..43
    internal const int OffKierownik = 46;  // 46..47

    internal const int BandHeight = 3;
    internal const int BandCount = 7;

    /// <summary>The row captions of the pile table, top to bottom.</summary>
    internal static readonly string[] BandLabels =
    {
        "Numer pala",
        "Średnica pala [m]",
        "Długość pala wg projektu [m]",
        "Długość wykonanego pala [m]",
        "Ilość betonu wbudowanego [m3]",
        "Beton z betoniarni:",
        "Zbrojenie:"
    };

    /// <summary>
    /// Writes one workbook covering every day in the journal. Days are ordered by
    /// date and never share a page, so each metryka carries a single DATA value -
    /// the day those piles were actually poured.
    /// </summary>
    public void Write(string path, IReadOnlyList<WorkDay> days, MetrykaSettings settings)
    {
        var pages = Paginate(days, settings.PilesPerPage);
        if (pages.Count == 0)
            throw new InvalidOperationException("Dziennik jest pusty — brak pali do wygenerowania.");

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Metryki");

        ConfigureSheet(ws, settings);

        for (var page = 0; page < pages.Count; page++)
        {
            var baseRow = page * RowsPerBlock;
            WriteBlock(ws, baseRow, pages[page].Piles, pages[page].Date, settings);

            if (page < pages.Count - 1)
                ws.PageSetup.AddHorizontalPageBreak(baseRow + RowsPerBlock);
        }

        ws.PageSetup.PrintAreas.Add(1, 1, pages.Count * RowsPerBlock, LastDataColumn);
        wb.SaveAs(path);
    }

    /// <summary>Splits each day into pages of at most <paramref name="perPage"/> piles.</summary>
    public static List<(DateTime Date, List<Pile> Piles)> Paginate(IReadOnlyList<WorkDay> days, int perPage)
    {
        perPage = Math.Max(1, perPage);
        var pages = new List<(DateTime, List<Pile>)>();

        foreach (var day in days.Where(d => d.Piles.Count > 0).OrderBy(d => d.Date))
            foreach (var chunk in day.Piles.OrderBy(p => p.Number).Chunk(perPage))
                pages.Add((day.Date, chunk.ToList()));

        return pages;
    }

    // --------------------------------------------------------------- layout

    private static void ConfigureSheet(IXLWorksheet ws, MetrykaSettings settings)
    {
        ws.Column(1).Width = 20.71;
        for (var c = FirstDataColumn; c <= LastDataColumn; c++)
            ws.Column(c).Width = 8.2;

        var ps = ws.PageSetup;
        ps.PaperSize = XLPaperSize.A4Paper;
        ps.PageOrientation = XLPageOrientation.Portrait;

        // Fit all 13 columns onto one page width, leaving the page count vertical
        // so that the manual row breaks below decide where pages end. Scaling by a
        // fixed percentage instead would push columns L and M onto a second page
        // whenever the font metrics differ slightly from the reference machine.
        ps.FitToPages(1, 0);
        ps.Margins.Left = 0.787;
        ps.Margins.Right = 0.787;
        ps.Margins.Top = 0.591;
        ps.Margins.Bottom = 0.551;
        ps.Margins.Header = 0.315;
        ps.Margins.Footer = 0.315;

        ps.Header.Left.AddText("BUDOWA: " + StripTrailingDot(settings.Budowa));
        ps.Header.Right.AddText(settings.DokumentacjaNaglowek);
        ps.Footer.Left.AddText(settings.Firma);
        ps.Footer.Right.AddText(XLHFPredefinedText.PageNumber);
    }

    private static void WriteBlock(IXLWorksheet ws, int baseRow, IReadOnlyList<Pile> piles, DateTime date, MetrykaSettings s)
    {
        for (var r = baseRow + 1; r <= baseRow + RowsPerBlock; r++)
            ws.Row(r).Height = 15.75;

        // Thin rule that separates the printed page header from the body.
        var rule = ws.Range(baseRow + OffRule, 1, baseRow + OffRule + 1, LastDataColumn).Merge();
        rule.Style.Border.TopBorder = XLBorderStyleValues.Thin;

        var title = ws.Range(baseRow + OffTitle, 1, baseRow + OffTitle + 1, LastDataColumn).Merge();
        title.FirstCell().Value = "METRYKA PALI";
        title.Style.Font.SetFontName("Calibri").Font.SetFontSize(16).Font.SetBold();
        title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        title.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        TextLine(ws, baseRow + OffMetoda, "METODA: " + s.Metoda);
        TextLine(ws, baseRow + OffWykonawca, "WYKONAWCA: " + s.Wykonawca);
        TextLine(ws, baseRow + OffBudowa, "BUDOWA: " + s.Budowa);

        var dataLabel = ws.Cell(baseRow + OffData, 2);
        dataLabel.Value = "DATA:";
        dataLabel.Style.Font.SetFontName("Calibri").Font.SetFontSize(12);
        dataLabel.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

        var dataValue = ws.Range(baseRow + OffData, 3, baseRow + OffData, 5).Merge();
        dataValue.FirstCell().Value = date;
        dataValue.FirstCell().Style.DateFormat.Format = "dd/MM/yyyy";
        dataValue.Style.Font.SetFontName("Calibri").Font.SetFontSize(12);
        dataValue.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        dataValue.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

        WriteTable(ws, baseRow, piles);

        var uwagi = ws.Range(baseRow + OffUwagi, 2, baseRow + OffUwagi + 4, 11).Merge();
        uwagi.FirstCell().Value = "UWAGI:";
        StyleFreeText(uwagi);

        var kierownik = ws.Range(baseRow + OffKierownik, 2, baseRow + OffKierownik + 1, 11).Merge();
        kierownik.FirstCell().Value = "KIEROWNIK ROBÓT PALOWYCH:";
        StyleFreeText(kierownik);
    }

    private static void WriteTable(IXLWorksheet ws, int baseRow, IReadOnlyList<Pile> piles)
    {
        for (var band = 0; band < BandCount; band++)
        {
            var top = baseRow + OffTable + band * BandHeight;
            var bottom = top + BandHeight - 1;

            var label = ws.Range(top, 1, bottom, 1).Merge();
            label.FirstCell().Value = BandLabels[band];
            StyleCell(label, fontSize: 12);

            for (var i = 0; i < LastDataColumn - FirstDataColumn + 1; i++)
            {
                var col = FirstDataColumn + i;
                var cell = ws.Range(top, col, bottom, col).Merge();

                if (i < piles.Count)
                    SetValue(cell.FirstCell(), band, piles[i]);

                StyleCell(cell, fontSize: 11);
            }
        }

        // Thin grid inside, medium box around the whole table.
        var table = ws.Range(baseRow + OffTable, 1, baseRow + OffTable + BandCount * BandHeight - 1, LastDataColumn);
        table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        table.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
    }

    private static void SetValue(IXLCell cell, int band, Pile pile)
    {
        switch (band)
        {
            case 0: cell.Value = pile.Number; break;
            case 1: cell.Value = pile.Diameter; break;
            case 2: cell.Value = pile.DesignLength; break;
            case 3: cell.Value = pile.ActualLength; break;
            case 4: cell.Value = pile.Concrete; break;
            case 5: cell.Value = pile.ConcretePlant; break;
            case 6: cell.Value = pile.Reinforcement; break;
        }
    }

    // ---------------------------------------------------------------- styles

    private static void TextLine(IXLWorksheet ws, int row, string text)
    {
        var range = ws.Range(row, 2, row + 1, 11).Merge();
        range.FirstCell().Value = text;
        StyleFreeText(range);
    }

    private static void StyleFreeText(IXLRange range)
    {
        range.Style.Font.SetFontName("Calibri").Font.SetFontSize(12);
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        range.Style.Alignment.WrapText = true;
    }

    private static void StyleCell(IXLRange range, int fontSize)
    {
        range.Style.Font.SetFontName("Calibri").Font.SetFontSize(fontSize);
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Alignment.WrapText = true;
    }

    internal static string StripTrailingDot(string text)
        => text.EndsWith('.') ? text[..^1] : text;
}
