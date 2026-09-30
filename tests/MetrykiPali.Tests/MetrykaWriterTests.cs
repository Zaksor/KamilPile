using ClosedXML.Excel;

namespace MetrykiPali.Tests;

/// <summary>
/// Reads the generated workbook back and checks the layout the reference
/// documentation uses: 48-row page blocks, twelve piles per sheet, one date per
/// sheet. Getting this wrong produces paperwork that looks right in the grid but
/// prints across two pages or under the wrong day.
/// </summary>
public sealed class MetrykaWriterTests : IDisposable
{
    private const int RowsPerBlock = 48;
    private const int RowNumery = 15;
    private const int RowSrednica = 18;
    private const int RowDlProjekt = 21;
    private const int RowDlWykonana = 24;
    private const int RowBeton = 27;
    private const int RowBetoniarnia = 30;
    private const int RowZbrojenie = 33;
    private const int RowData = 12;
    private const int RowTytul = 3;

    private readonly string _path = Path.Combine(
        Path.GetTempPath(), "mpali-" + Guid.NewGuid().ToString("N") + ".xlsx");

    public void Dispose()
    {
        try { File.Delete(_path); } catch { /* best effort */ }
    }

    private static readonly DateTime D12 = new(2022, 9, 12);
    private static readonly DateTime D13 = new(2022, 9, 13);

    private static Pile P(int n, DateTime day, double length = 9) => new()
    {
        Number = n,
        Diameter = 0.4,
        DesignLength = length,
        ActualLength = length,
        Concrete = PileMath.Concrete(0.4, length, 1.30),
        ConcretePlant = "Bosta",
        Reinforcement = "Brak",
        Executed = day
    };

    private static WorkDay Day(DateTime date, IEnumerable<int> numbers, double length = 9)
        => new() { Date = date, Piles = numbers.Select(n => P(n, date, length)).ToList() };

    private IXLWorksheet Generate(params WorkDay[] days)
    {
        Writer.Write(_path, days, new MetrykaSettings());
        return new XLWorkbook(_path).Worksheet(1);
    }

    private static int BlockRow(int page, int offset) => (page - 1) * RowsPerBlock + offset;

    // ----------------------------------------------------------------- shape

    [Fact]
    public void Opens_in_page_break_preview_so_the_printed_pages_show()
    {
        var ws = Generate(Day(D12, Enumerable.Range(1, 12)), Day(D13, Enumerable.Range(13, 12)));

        // ClosedXML writes the view but does not read it back, so look at the sheet XML.
        using var zip = System.IO.Compression.ZipFile.OpenRead(_path);
        using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        Assert.Contains("view=\"pageBreakPreview\"", reader.ReadToEnd());
        Assert.Equal(new[] { 48 }, ws.PageSetup.RowBreaks);
    }

    [Fact]
    public void Writes_a_file_that_opens_as_a_workbook()
    {
        var ws = Generate(Day(D12, Enumerable.Range(1, 12)));

        Assert.True(File.Exists(_path));
        Assert.Equal("METRYKA PALI", ws.Cell(RowTytul, 1).GetString());
    }

    [Fact]
    public void Puts_the_labels_where_the_reference_has_them()
    {
        var ws = Generate(Day(D12, Enumerable.Range(1, 12)));

        Assert.Equal("Numer pala", ws.Cell(RowNumery, 1).GetString());
        Assert.Equal("Średnica pala [m]", ws.Cell(RowSrednica, 1).GetString());
        Assert.Equal("Długość pala wg projektu [m]", ws.Cell(RowDlProjekt, 1).GetString());
        Assert.Equal("Długość wykonanego pala [m]", ws.Cell(RowDlWykonana, 1).GetString());
        Assert.Equal("Ilość betonu wbudowanego [m3]", ws.Cell(RowBeton, 1).GetString());
        Assert.Equal("Beton z betoniarni:", ws.Cell(RowBetoniarnia, 1).GetString());
        Assert.Equal("Zbrojenie:", ws.Cell(RowZbrojenie, 1).GetString());
    }

    [Fact]
    public void Writes_twelve_piles_across_columns_B_to_M()
    {
        var ws = Generate(Day(D12, Enumerable.Range(101, 12)));

        for (var i = 0; i < 12; i++)
            Assert.Equal(101 + i, ws.Cell(RowNumery, 2 + i).GetValue<int>());
    }

    [Fact]
    public void Writes_every_row_of_a_pile_column()
    {
        var ws = Generate(Day(D12, new[] { 7 }, length: 8));

        Assert.Equal(7, ws.Cell(RowNumery, 2).GetValue<int>());
        Assert.Equal(0.4, ws.Cell(RowSrednica, 2).GetValue<double>());
        Assert.Equal(8, ws.Cell(RowDlProjekt, 2).GetValue<double>());
        Assert.Equal(8, ws.Cell(RowDlWykonana, 2).GetValue<double>());
        Assert.Equal(1.31, ws.Cell(RowBeton, 2).GetValue<double>());
        Assert.Equal("Bosta", ws.Cell(RowBetoniarnia, 2).GetString());
        Assert.Equal("Brak", ws.Cell(RowZbrojenie, 2).GetString());
    }

    [Fact]
    public void Leaves_the_unused_columns_of_a_short_page_empty()
    {
        var ws = Generate(Day(D12, new[] { 1, 2, 3 }));

        Assert.Equal(3, ws.Cell(RowNumery, 4).GetValue<int>());
        for (var col = 5; col <= 13; col++)
            Assert.True(ws.Cell(RowNumery, col).IsEmpty(), $"column {col} should be empty");
    }

    // ------------------------------------------------------------ pagination

    [Fact]
    public void Starts_each_page_one_block_further_down()
    {
        var ws = Generate(
            Day(D12, Enumerable.Range(1, 12)),
            Day(D13, Enumerable.Range(13, 12)));

        Assert.Equal("METRYKA PALI", ws.Cell(BlockRow(2, RowTytul), 1).GetString());
        Assert.Equal(13, ws.Cell(BlockRow(2, RowNumery), 2).GetValue<int>());
    }

    [Fact]
    public void Gives_each_page_the_date_its_piles_were_poured()
    {
        var ws = Generate(
            Day(D12, Enumerable.Range(1, 12)),
            Day(D13, Enumerable.Range(13, 18)));

        Assert.Equal(D12, ws.Cell(BlockRow(1, RowData), 3).GetDateTime());
        Assert.Equal(D13, ws.Cell(BlockRow(2, RowData), 3).GetDateTime());
        Assert.Equal(D13, ws.Cell(BlockRow(3, RowData), 3).GetDateTime());   // spill page
    }

    [Fact]
    public void Splits_an_eighteen_pile_day_into_twelve_and_six()
    {
        var ws = Generate(Day(D13, Enumerable.Range(1, 18)));

        Assert.Equal(12, ws.Cell(BlockRow(1, RowNumery), 13).GetValue<int>());   // column M
        Assert.Equal(13, ws.Cell(BlockRow(2, RowNumery), 2).GetValue<int>());    // column B
        Assert.Equal(18, ws.Cell(BlockRow(2, RowNumery), 7).GetValue<int>());
        Assert.True(ws.Cell(BlockRow(2, RowNumery), 8).IsEmpty());
    }

    [Fact]
    public void Breaks_the_print_between_every_pair_of_pages()
    {
        var ws = Generate(
            Day(D12, Enumerable.Range(1, 12)),
            Day(D13, Enumerable.Range(13, 12)));

        Assert.Contains(RowsPerBlock, ws.PageSetup.RowBreaks);
        Assert.Single(ws.PageSetup.RowBreaks);    // no trailing break after the last page
    }

    // --------------------------------------------------------------- printing

    [Fact]
    public void Sets_up_an_A4_portrait_page_that_fits_the_columns()
    {
        var ws = Generate(Day(D12, Enumerable.Range(1, 12)));

        Assert.Equal(XLPaperSize.A4Paper, ws.PageSetup.PaperSize);
        Assert.Equal(XLPageOrientation.Portrait, ws.PageSetup.PageOrientation);

        // One page wide, unlimited tall: a fixed scale would push columns L and
        // M onto a second sheet on a machine with different font metrics.
        Assert.Equal(1, ws.PageSetup.PagesWide);
        Assert.Equal(0, ws.PageSetup.PagesTall);
    }

    [Fact]
    public void Repeats_the_site_and_company_in_the_page_header_and_footer()
    {
        var settings = new MetrykaSettings { Budowa = "Testowa budowa.", Firma = "FIRMA SP. Z O.O." };
        Writer.Write(_path, new[] { Day(D12, new[] { 1 }) }, settings);

        using var wb = new XLWorkbook(_path);
        var ps = wb.Worksheet(1).PageSetup;

        Assert.Contains("Testowa budowa", ps.Header.Left.GetText(XLHFOccurrence.OddPages));
        Assert.Contains("DOKUMENTACJA POWYKONAWCZA", ps.Header.Right.GetText(XLHFOccurrence.OddPages));
        Assert.Contains("FIRMA SP. Z O.O.", ps.Footer.Left.GetText(XLHFOccurrence.OddPages));
        Assert.Contains("Testowa budowa", ps.Header.Left.GetText(XLHFOccurrence.EvenPages));
    }

    [Fact]
    public void Prints_the_header_lines_from_the_settings()
    {
        var settings = new MetrykaSettings
        {
            Budowa = "Budynek przy ul. Łódzkiej.",
            Wykonawca = "Wykonawca sp. z o.o.",
            Metoda = "CFA"
        };
        Writer.Write(_path, new[] { Day(D12, new[] { 1 }) }, settings);

        using var wb = new XLWorkbook(_path);
        var ws = wb.Worksheet(1);

        Assert.Equal("METODA: CFA", ws.Cell(6, 2).GetString());
        Assert.Equal("WYKONAWCA: Wykonawca sp. z o.o.", ws.Cell(8, 2).GetString());
        Assert.Equal("BUDOWA: Budynek przy ul. Łódzkiej.", ws.Cell(10, 2).GetString());
        Assert.Equal("DATA:", ws.Cell(12, 2).GetString());
    }

    [Fact]
    public void Leaves_room_for_notes_and_the_supervisor_signature()
    {
        var ws = Generate(Day(D12, new[] { 1 }));

        Assert.Equal("UWAGI:", ws.Cell(39, 2).GetString());
        Assert.Equal("KIEROWNIK ROBÓT PALOWYCH:", ws.Cell(46, 2).GetString());
    }

    [Fact]
    public void Boxes_the_table_so_it_prints_as_a_grid()
    {
        var ws = Generate(Day(D12, Enumerable.Range(1, 12)));

        Assert.Equal(XLBorderStyleValues.Medium, ws.Cell(RowNumery, 1).Style.Border.TopBorder);
        Assert.Equal(XLBorderStyleValues.Medium, ws.Cell(RowNumery, 1).Style.Border.LeftBorder);
        Assert.NotEqual(XLBorderStyleValues.None, ws.Cell(RowSrednica, 5).Style.Border.TopBorder);
    }

    // -------------------------------------------------------- whole document

    [Fact]
    public void Generates_one_page_per_twelve_piles_across_many_days()
    {
        var days = Enumerable.Range(0, 5)
            .Select(i => Day(D12.AddDays(i), Enumerable.Range(i * 12 + 1, 12)))
            .ToArray();

        var ws = Generate(days);

        for (var page = 1; page <= 5; page++)
        {
            Assert.Equal("METRYKA PALI", ws.Cell(BlockRow(page, RowTytul), 1).GetString());
            Assert.Equal(D12.AddDays(page - 1), ws.Cell(BlockRow(page, RowData), 3).GetDateTime());
        }
        Assert.Equal(4, ws.PageSetup.RowBreaks.Count);
    }

    [Fact]
    public void Survives_a_realistic_schedule()
    {
        var days = Enumerable.Range(0, 40)
            .Select(i => Day(D12.AddDays(i), Enumerable.Range(i * 24 + 1, 24)))
            .ToArray();

        Writer.Write(_path, days, new MetrykaSettings());

        using var wb = new XLWorkbook(_path);
        var ws = wb.Worksheet(1);

        Assert.Equal(960, days.Sum(d => d.Piles.Count));
        Assert.Equal(79, ws.PageSetup.RowBreaks.Count);          // 80 pages
        Assert.Equal("METRYKA PALI", ws.Cell(BlockRow(80, RowTytul), 1).GetString());
    }
}
