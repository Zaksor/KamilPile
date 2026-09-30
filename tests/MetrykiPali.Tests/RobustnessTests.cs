using ClosedXML.Excel;

namespace MetrykiPali.Tests;

/// <summary>
/// Cases found by probing the readers and the store rather than by design. Each
/// one crashed, dropped data or corrupted a date before it was fixed.
/// </summary>
public class RobustnessTests
{
    private static string Temp(string ext) => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ext);

    // ------------------------------------------------------------ bad files

    /// <summary>
    /// The application only handles IOException, InvalidDataException,
    /// NotSupportedException and UnauthorizedAccessException when loading. A
    /// reader that throws anything else takes the whole window down, so a file
    /// that is not a workbook must surface as one of those.
    /// </summary>
    [Fact]
    public void A_file_that_is_not_a_workbook_fails_in_a_way_the_app_can_report()
    {
        var path = Temp(".xlsx");
        File.WriteAllBytes(path, new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 1, 2, 3, 4, 5, 6, 7, 8 });
        try
        {
            var thrown = Record.Exception(() => Reader.Read(path));

            Assert.IsType<InvalidDataException>(thrown);
            Assert.Contains("uszkodzony", thrown!.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_damaged_xls_file_fails_in_a_way_the_app_can_report()
    {
        var path = Temp(".xls");
        File.WriteAllBytes(path, new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 1, 2, 3, 4 });
        try
        {
            var thrown = Record.Exception(() => Reader.Read(path));

            Assert.IsType<InvalidDataException>(thrown);
            Assert.Contains(".xls", thrown!.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void An_empty_workbook_reports_the_columns_it_wanted()
    {
        var path = Temp(".xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                wb.AddWorksheet("Arkusz1").Cell(1, 1).Value = "nic tu nie ma";
                wb.SaveAs(path);
            }

            var thrown = Record.Exception(() => Reader.Read(path));

            Assert.IsType<InvalidDataException>(thrown);
            Assert.Contains("rednica", thrown!.Message);
        }
        finally { File.Delete(path); }
    }

    // --------------------------------------------------------- real layouts

    [Fact]
    public void A_schedule_behind_a_cover_sheet_is_found()
    {
        var path = Temp(".xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                wb.AddWorksheet("Okladka").Cell(1, 1).Value = "Projekt posadowienia";
                var ws = wb.AddWorksheet("Pale");
                ws.Cell(1, 1).Value = "Numer pala";
                ws.Cell(2, 1).Value = 1;
                ws.Cell(2, 2).Value = 10;
                ws.Cell(2, 3).Value = 0.4;
                ws.Cell(2, 4).Value = 7;
                ws.Cell(2, 5).Value = "Brak";
                wb.SaveAs(path);
            }

            var ranges = Reader.Read(path);

            Assert.Single(ranges);
            Assert.Equal(10, ranges[0].To);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_table_that_does_not_start_in_column_A_is_read()
    {
        var path = Temp(".xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("Arkusz1");
                ws.Cell(1, 3).Value = "Numer pala";
                ws.Cell(2, 3).Value = 1;
                ws.Cell(2, 4).Value = 10;
                ws.Cell(2, 5).Value = 0.4;
                ws.Cell(2, 6).Value = 7;
                ws.Cell(2, 7).Value = "Brak";
                wb.SaveAs(path);
            }

            Assert.Single(Reader.Read(path));
        }
        finally { File.Delete(path); }
    }

    // ---------------------------------------------------------- pour dates

    /// <summary>
    /// A pour date is a calendar day, not an instant. Stored with a timezone
    /// offset it moves by a day when the project is opened in another zone -
    /// and the date is the whole point of a metryka.
    /// </summary>
    [Fact]
    public void A_pour_date_is_stored_without_a_timezone_offset()
    {
        var path = Temp(".mpali");
        var poured = new DateTime(2022, 9, 12, 0, 0, 0, DateTimeKind.Local);
        try
        {
            Repository.Save(path, new ProjectState
            {
                Settings = new MetrykaSettings { Data = poured },
                Piles = { new Pile { Number = 1, Executed = poured } }
            });

            var json = File.ReadAllText(path);

            Assert.Contains("\"2022-09-12T00:00:00\"", json);
            Assert.DoesNotContain("2022-09-12T00:00:00+", json);
            Assert.DoesNotContain("2022-09-11", json);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_pour_date_reads_back_as_the_same_calendar_day()
    {
        var path = Temp(".mpali");
        try
        {
            Repository.Save(path, new ProjectState
            {
                Piles = { new Pile { Number = 1, Executed = new DateTime(2022, 9, 12, 0, 0, 0, DateTimeKind.Local) } }
            });

            var loaded = Repository.Load(path)!;

            Assert.Equal(new DateTime(2022, 9, 12), loaded.Piles[0].Executed!.Value.Date);
            Assert.Equal(DateTimeKind.Unspecified, loaded.Piles[0].Executed!.Value.Kind);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Projects_written_before_the_date_fix_still_open()
    {
        // Files already on disk carry an offset; they must keep working.
        var path = Temp(".mpali");
        File.WriteAllText(path, """
        {
          "Version": 1,
          "Settings": { "Data": "2022-09-13T00:00:00+02:00" },
          "Ranges": [],
          "Piles": [ { "Number": 1, "Executed": "2022-09-12T00:00:00+02:00" } ]
        }
        """);
        try
        {
            var loaded = Repository.Load(path);

            Assert.NotNull(loaded);
            Assert.Equal(new DateTime(2022, 9, 12), loaded!.Piles[0].Executed!.Value.Date);
        }
        finally { File.Delete(path); }
    }
}
