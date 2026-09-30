using UglyToad.PdfPig;

namespace MetrykiPali.Tests;

/// <summary>
/// The PDF is read back with PdfPig - the same library the app reads schedules
/// with - so these check what a reader of the file actually gets.
/// </summary>
public sealed class MetrykaPdfWriterTests : IDisposable
{
    private static readonly DateTime D12 = new(2022, 9, 12);
    private static readonly DateTime D13 = new(2022, 9, 13);

    private readonly string _path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
    private readonly MetrykaSettings _settings = new();

    public void Dispose()
    {
        try { File.Delete(_path); } catch { /* best effort */ }
    }

    private static WorkDay Day(DateTime date, IEnumerable<int> numbers, double length = 7) => new()
    {
        Date = date,
        Piles = numbers.Select(n => new Pile
        {
            Number = n, Diameter = 0.4, DesignLength = length, ActualLength = length,
            Concrete = PileMath.Concrete(0.4, length, 1.30), ConcretePlant = "Bosta", Reinforcement = "Brak"
        }).ToList()
    };

    private List<string> PageTexts(params WorkDay[] days)
    {
        new MetrykaPdfWriter().Write(_path, days, _settings);
        using var pdf = PdfDocument.Open(_path);
        return pdf.GetPages().Select(p => string.Join(" ", p.GetWords().Select(w => w.Text))).ToList();
    }

    [Fact]
    public void Writes_one_page_per_twelve_piles_and_splits_a_long_day()
    {
        var pages = PageTexts(Day(D12, Enumerable.Range(1, 12)), Day(D13, Enumerable.Range(13, 18)));

        Assert.Equal(3, pages.Count);
    }

    [Fact]
    public void Each_page_carries_the_date_of_its_day()
    {
        var pages = PageTexts(Day(D12, Enumerable.Range(1, 12)), Day(D13, Enumerable.Range(13, 18)));

        Assert.Contains("12.09.2022", pages[0]);
        Assert.Contains("13.09.2022", pages[1]);
        Assert.Contains("13.09.2022", pages[2]);
    }

    [Fact]
    public void A_page_holds_the_title_the_header_lines_and_the_table()
    {
        var page = PageTexts(Day(D12, new[] { 7 }, length: 8))[0];

        Assert.Contains("METRYKA PALI", page);
        Assert.Contains("METODA: " + _settings.Metoda, page);
        Assert.Contains("WYKONAWCA:", page);
        Assert.Contains("KIEROWNIK ROBÓT PALOWYCH:", page);
        Assert.Contains("Ilość betonu", page);
        Assert.Contains("1,31", page);          // 0.4 m x 8 m, as Excel on a Polish Windows shows it
        Assert.Contains("0,4", page);
        Assert.Contains("Bosta", page);
    }

    [Fact]
    public void Polish_characters_survive()
    {
        _settings.Budowa = "Łódź, ul. Żółkiewskiego — ęąśćń.";

        var page = PageTexts(Day(D12, new[] { 1 }))[0];

        Assert.Contains("Łódź, ul. Żółkiewskiego", page);
        Assert.Contains("ęąśćń", page);
    }

    [Fact]
    public void Pages_are_numbered_through_the_whole_file()
    {
        var pages = PageTexts(Day(D12, Enumerable.Range(1, 12)), Day(D13, Enumerable.Range(13, 12)));

        Assert.EndsWith("1", pages[0]);
        Assert.EndsWith("2", pages[1]);
    }

    [Fact]
    public void Pages_are_a4_portrait()
    {
        new MetrykaPdfWriter().Write(_path, new[] { Day(D12, new[] { 1 }) }, _settings);
        using var pdf = PdfDocument.Open(_path);

        var page = pdf.GetPage(1);
        Assert.Equal(595, page.Width, 0);
        Assert.Equal(842, page.Height, 0);
    }

    [Fact]
    public void An_empty_journal_is_refused_in_a_way_the_app_reports()
        => Assert.Throws<InvalidOperationException>(() =>
            new MetrykaPdfWriter().Write(_path, Array.Empty<WorkDay>(), _settings));
}

/// <summary>The file name decides the format; the presenter only picks the name.</summary>
public class MetrykaFileWriterTests
{
    private readonly RecordingMetrykaWriter _xlsx = new();
    private readonly RecordingMetrykaWriter _pdf = new();

    [Theory]
    [InlineData(@"C:\wyjscie\metryki.pdf", true)]
    [InlineData(@"C:\wyjscie\METRYKI.PDF", true)]
    [InlineData(@"C:\wyjscie\metryki.xlsx", false)]
    [InlineData(@"C:\wyjscie\metryki", false)]
    public void Pdf_files_go_to_the_pdf_writer_and_everything_else_to_the_workbook(string path, bool pdf)
    {
        new MetrykaFileWriter(_xlsx, _pdf).Write(path, Array.Empty<WorkDay>(), new MetrykaSettings());

        Assert.Equal(pdf ? 1 : 0, _pdf.Calls);
        Assert.Equal(pdf ? 0 : 1, _xlsx.Calls);
    }
}
