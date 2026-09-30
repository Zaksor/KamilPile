using System.Drawing;
using System.IO.Compression;
using ClosedXML.Excel;
using UglyToad.PdfPig;

namespace MetrykiPali.Tests;

/// <summary>
/// Which piles still have no metryka, the order of the journal, and the
/// footer - text and picture - in both output formats.
/// </summary>
public sealed class JournalToolsTests : IDisposable
{
    private readonly FakeMainView _view = new();
    private readonly StubScheduleReader _reader = new((1, 12, 0.4, 7), (13, 24, 0.4, 8), (25, 36, 0.4, 9));
    private readonly RecordingMetrykaWriter _writer = new();
    private readonly InMemoryProjectRepository _repository = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mpali-tools-" + Guid.NewGuid().ToString("N"));

    private static readonly DateTime D12 = new(2022, 9, 12);
    private static readonly DateTime D13 = new(2022, 9, 13);

    public JournalToolsTests()
    {
        Directory.CreateDirectory(_dir);
        new MainPresenter(_view, _reader, _writer, _repository).Start();
        _view.SchedulePath = @"C:\budowa\tabelka.xlsx";
        _view.ClickLoadSchedule();
        _view.MetrykiPath = @"C:\wyjscie\metryki.xlsx";
        _view.AnswerConfirm = (title, _) => title != "Gotowe";
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string Report()
    {
        _view.ClickMissingMetryki();
        return _view.Reports[^1].Text;
    }

    // ------------------------------------------------------- missing metryki

    [Fact]
    public void Before_anything_is_logged_every_pile_is_listed_as_undated()
    {
        var report = Report();

        Assert.Contains("PALE BEZ DATY WYKONANIA (nie ma ich jeszcze w dzienniku): 36", report);
        Assert.Contains("1-36", report);
        Assert.Equal("Pale bez metryk: 36 — pokaż które", _view.MissingMetrykiText);
    }

    [Fact]
    public void A_logged_day_is_listed_until_its_metryki_are_generated()
    {
        _view.LogDay(D12, "1-12");
        Assert.Contains("12.09.2022  —  12 z 12 pali:  1-12", Report());
        Assert.Equal("nie", _view.Journal[0].Metryki);

        _view.ClickGenerateSelectedDays(D12);

        Assert.DoesNotContain("12.09.2022", Report());
        Assert.Equal(DateTime.Today.ToString("dd.MM.yyyy"), _view.Journal[0].Metryki);
        Assert.Equal("Pale bez metryk: 24 — pokaż które", _view.MissingMetrykiText);
    }

    [Fact]
    public void When_everything_is_done_the_report_says_so()
    {
        _view.LogDay(D12, "1-36");
        _view.ClickGenerate();

        Assert.Contains("Wszystkie pale mają daty wykonania i wygenerowane metryki", Report());
        Assert.Equal("Wszystkie pale mają metryki ✓", _view.MissingMetrykiText);
    }

    [Fact]
    public void Adding_a_pile_to_a_generated_day_leaves_only_that_pile_missing()
    {
        _view.LogDay(D12, "1-10");
        _view.ClickGenerate();

        _view.LogDay(D12, "11");

        Assert.Equal("częściowo", _view.Journal[0].Metryki);
        Assert.Contains("12.09.2022  —  1 z 11 pali:  11", Report());
    }

    [Fact]
    public void Changing_a_days_coefficient_after_generating_marks_it_missing_again()
    {
        _view.LogDay(D12, "1-12");
        _view.ClickGenerate();

        _view.EditDayFactor(D12, 1.40);

        Assert.Equal("nie", _view.Journal[0].Metryki);
    }

    [Fact]
    public void Correcting_a_piles_length_after_generating_marks_that_pile_missing()
    {
        _view.LogDay(D12, "1-12");
        _view.ClickGenerate();

        _view.Piles[4].ActualLength = 7.5;
        _view.EditPile(4, nameof(Pile.ActualLength));

        Assert.Contains("12.09.2022  —  1 z 12 pali:  5", Report());
    }

    [Fact]
    public void A_failed_write_marks_nothing_as_generated()
    {
        _view.LogDay(D12, "1-12");
        _writer.FailWith = new IOException("zablokowany");

        _view.ClickGenerate();

        Assert.Equal("nie", _view.Journal[0].Metryki);
    }

    [Fact]
    public void What_was_generated_is_remembered_next_time()
    {
        _view.LogDay(D12, "1-12");
        _view.ClickGenerate();

        var next = new FakeMainView();
        new MainPresenter(next, _reader, _writer, _repository).Start();

        Assert.Equal(DateTime.Today.ToString("dd.MM.yyyy"), next.Journal[0].Metryki);
    }

    // ------------------------------------------------------------ journal order

    [Fact]
    public void The_journal_can_list_the_newest_day_first_and_remembers_it()
    {
        _view.LogDay(D12, "1-5");
        _view.LogDay(D13, "6-10");
        Assert.Equal(new[] { D12, D13 }, _view.Journal.Select(e => e.Data));

        _view.ChangeJournalOrder(newestFirst: true);
        Assert.Equal(new[] { D13, D12 }, _view.Journal.Select(e => e.Data));

        var next = new FakeMainView();
        new MainPresenter(next, _reader, _writer, _repository).Start();
        Assert.True(next.JournalNewestFirst);
        Assert.Equal(new[] { D13, D12 }, next.Journal.Select(e => e.Data));
    }

    [Fact]
    public void The_order_on_screen_does_not_change_the_order_of_the_metryki()
    {
        _view.LogDay(D12, "1-5");
        _view.LogDay(D13, "6-10");
        _view.ChangeJournalOrder(newestFirst: true);

        _view.ClickGenerate();

        Assert.Equal(new[] { D12, D13 }, _writer.Days.Select(d => d.Date));
    }

    [Fact]
    public void A_day_on_the_coefficient_shows_its_concrete_as_used_too()
    {
        _view.LogDay(D12, "1-12");                        // 12 × Ø 0.4 × 7 m at 1.30

        var entry = _view.Journal[0];
        Assert.False(entry.Mierzone);
        Assert.Equal(Math.Round(12 * PileMath.Concrete(0.4, 7, 1.30), 2), entry.Zuzyto);
        Assert.Equal(entry.Beton, entry.Zuzyto);
        Assert.Equal(entry.Zuzyto, _view.Progress!.ConcreteLogged);
    }

    // ------------------------------------------------------------------ footer

    private string MakeImage(string name, int width = 300, int height = 100)
    {
        var path = Path.Combine(_dir, name);
        using var bitmap = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bitmap)) g.Clear(Color.SteelBlue);
        bitmap.Save(path, name.EndsWith(".png") ? System.Drawing.Imaging.ImageFormat.Png : System.Drawing.Imaging.ImageFormat.Jpeg);
        return path;
    }

    [Fact]
    public void The_footer_text_and_picture_reach_the_writer_and_are_kept_with_the_site()
    {
        _view.LogDay(D12, "1-12");
        _view.ChangeFooter("Firma Testowa & Syn", FooterPosition.Left);
        _view.ChooseFooterImage(MakeImage("logo.jpg"));

        _view.ClickGenerate();

        Assert.Equal("Firma Testowa & Syn", _writer.Settings!.Firma);
        Assert.Equal(FooterPosition.Left, _writer.Settings.FooterImagePosition);
        Assert.NotNull(_writer.Settings.FooterImage);
        Assert.Equal("logo.jpg", _view.FooterImageLabel);

        var saved = _repository.Load(_repository.SitePath(SiteNames.Fallback))!.Settings;
        Assert.Equal("logo.jpg", saved.FooterImageName);
        Assert.Equal(_writer.Settings.FooterImage, saved.FooterImage);
    }

    [Fact]
    public void A_file_that_is_not_a_picture_is_refused()
    {
        var path = Path.Combine(_dir, "nie-obraz.jpg");
        File.WriteAllText(path, "to nie jest obraz");

        _view.ChooseFooterImage(path);

        Assert.Contains(_view.Errors, e => e.Contains("Nie udało się wczytać obrazu"));
        Assert.Equal("brak", _view.FooterImageLabel);
    }

    [Fact]
    public void The_footer_picture_can_be_removed()
    {
        _view.ChooseFooterImage(MakeImage("logo.png"));

        _view.ClickClearFooterImage();

        Assert.Equal("brak", _view.FooterImageLabel);
        Assert.Null(_repository.Load(_repository.SitePath(SiteNames.Fallback))!.Settings.FooterImage);
    }

    [Fact]
    public void A_large_picture_is_scaled_down_and_stored_as_png()
    {
        var png = FooterImage.Prepare(MakeImage("duze.jpg", 3000, 1500));

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4));
        Assert.Equal((600, FooterImage.MaxHeightPixels), FooterImage.Size(png));
    }

    private static MetrykaSettings WithPicture(byte[] png, FooterPosition position) => new()
    {
        Firma = "Firma & Syn", FooterImage = png, FooterImageName = "logo.png", FooterImagePosition = position
    };

    private static WorkDay OneDay() => new()
    {
        Date = D12,
        Piles = Enumerable.Range(1, 3).Select(n => new Pile { Number = n, Diameter = 0.4, DesignLength = 7, ActualLength = 7 }).ToList()
    };

    [Theory]
    [InlineData(FooterPosition.Left, "LF", "&L&G&CFirma && Syn&R&P")]
    [InlineData(FooterPosition.Center, "CF", "&LFirma && Syn&C&G&R&P")]
    [InlineData(FooterPosition.Right, "RF", "&LFirma && Syn&C&P&R&G")]
    public void The_workbook_carries_the_picture_in_the_chosen_footer_section(FooterPosition position, string shape, string footer)
    {
        var path = Path.Combine(_dir, "stopka.xlsx");
        var png = FooterImage.Prepare(MakeImage("logo.png"));

        TestServices.Writer.Write(path, new[] { OneDay() }, WithPicture(png, position));

        using var zip = ZipFile.OpenRead(path);
        string Read(string name) { using var r = new StreamReader(zip.GetEntry(name)!.Open()); return r.ReadToEnd(); }

        var sheet = Read("xl/worksheets/sheet1.xml");
        Assert.Contains("legacyDrawingHF", sheet);
        Assert.Contains(System.Security.SecurityElement.Escape(footer), sheet);

        var vml = zip.Entries.Single(e => e.FullName.EndsWith(".vml"));
        using var vr = new StreamReader(vml.Open());
        Assert.Contains($"id=\"{shape}\"", vr.ReadToEnd());
        Assert.Contains(zip.Entries, e => e.FullName.EndsWith(".png") && e.Length == png.Length);

        using var wb = new XLWorkbook(path);     // still a workbook ClosedXML can open
        Assert.Equal("METRYKA PALI", wb.Worksheet(1).Cell(3, 1).GetString());
    }

    [Fact]
    public void Without_a_picture_the_workbook_footer_is_just_text_and_page_number()
    {
        var path = Path.Combine(_dir, "bez.xlsx");

        TestServices.Writer.Write(path, new[] { OneDay() }, new MetrykaSettings { Firma = "Firma Bez Logo" });

        using var zip = ZipFile.OpenRead(path);
        Assert.DoesNotContain(zip.Entries, e => e.FullName.EndsWith(".vml"));
        using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var sheet = reader.ReadToEnd();
        Assert.Contains("Firma Bez Logo", sheet);
        Assert.DoesNotContain("legacyDrawingHF", sheet);
    }

    [Fact]
    public void The_pdf_draws_the_footer_text_and_the_picture_on_every_page()
    {
        var path = Path.Combine(_dir, "stopka.pdf");
        var png = FooterImage.Prepare(MakeImage("logo.png"));
        var day = OneDay();
        day.Piles = Enumerable.Range(1, 20).Select(n => new Pile { Number = n, Diameter = 0.4, DesignLength = 7, ActualLength = 7 }).ToList();

        new MetrykaPdfWriter().Write(path, new[] { day }, WithPicture(png, FooterPosition.Center));

        using var pdf = PdfDocument.Open(path);
        Assert.Equal(2, pdf.NumberOfPages);
        foreach (var page in pdf.GetPages())
        {
            Assert.Single(page.GetImages());
            Assert.Contains("Firma & Syn", string.Join(" ", page.GetWords().Select(w => w.Text)));
        }
    }
}
