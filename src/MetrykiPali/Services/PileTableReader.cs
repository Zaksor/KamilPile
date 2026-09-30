using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

using MetrykiPali.Model;

namespace MetrykiPali.Services;

/// <summary>
/// Reads the source table ("tabelka z palami") from .xlsx / .xls / .csv / .pdf.
/// A spreadsheet holding a designer's summary table is read by its headers
/// (<see cref="DesignerTable"/>); otherwise, and for .csv and .pdf, the columns
/// are expected as od | do | średnica [m] | długość | zbrojenie.
/// </summary>
public sealed class PileTableReader : IScheduleReader
{
    public IReadOnlyList<PileRange> Read(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var ranges = ext switch
        {
            ".xlsx" or ".xlsm" or ".xls" => ReadExcel(path),
            ".csv" or ".txt" => ReadCsv(path),
            ".pdf" => ReadPdf(path),
            _ => throw new NotSupportedException($"Nieobsługiwany format pliku: {ext}")
        };

        if (ranges.Count == 0)
            throw new InvalidDataException(
                "Nie znaleziono żadnych zakresów pali. Tabelka powinna mieć nagłówki " +
                "\"NR PALI\", \"ŚREDNICA\" i \"DŁUGOŚĆ 1 PALA\" (zbrojenie opcjonalnie), " +
                "albo kolumny: numer od | numer do | średnica | długość pala | zbrojenie.");

        return ranges;
    }

    // ---------------------------------------------------------------- Excel

    private static List<PileRange> ReadExcel(string path)
    {
        // Copy first: the source file is often open in Excel / synced by OneDrive,
        // both of which hold a lock that would make a direct open fail.
        var temp = Path.Combine(Path.GetTempPath(), $"metryki_{Guid.NewGuid():N}{Path.GetExtension(path)}");
        File.Copy(path, temp, overwrite: true);
        try
        {
            // ClosedXML reads the Open XML formats only; a real .xls is the older
            // BIFF format, which designers still send, and ExcelDataReader reads it.
            var sheets = Path.GetExtension(path).Equals(".xls", StringComparison.OrdinalIgnoreCase)
                ? LegacyExcel.ReadSheets(temp)
                : OpenXmlSheets(temp);

            // Take the first sheet that actually holds a schedule. Real project
            // files often lead with a cover sheet, and the table sits behind it.
            InvalidDataException? refusal = null;
            foreach (var sheet in sheets)
            {
                try
                {
                    var result = ReadGrid(sheet);
                    if (result.Count > 0) return result;
                }
                catch (InvalidDataException ex)
                {
                    refusal ??= ex;   // a sheet recognised but unusable; another may still do
                }
            }

            if (refusal is not null) throw refusal;
            return new List<PileRange>();
        }
        finally
        {
            try { File.Delete(temp); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// One sheet, as the text of its cells. A designer's summary table is read
    /// by its headers; failing that, the first five columns are taken as
    /// od | do | średnica | długość | zbrojenie.
    /// </summary>
    private static List<PileRange> ReadGrid(IReadOnlyList<IReadOnlyList<string>> grid)
    {
        if (grid.Count == 0) return new List<PileRange>();

        var designed = DesignerTable.TryRead(grid);
        if (designed is { Count: > 0 }) return designed;

        var result = new List<PileRange>();
        var left = FirstUsedColumn(grid);
        foreach (var row in grid)
        {
            string At(int i) => left + i < row.Count ? row[left + i] : "";
            var range = TryBuildRange(At(0), At(1), At(2), At(3), At(4));
            if (range is not null) result.Add(range);
        }
        return result;
    }

    private static int FirstUsedColumn(IReadOnlyList<IReadOnlyList<string>> grid)
    {
        var first = int.MaxValue;
        foreach (var row in grid)
            for (var c = 0; c < Math.Min(row.Count, first); c++)
                if (!string.IsNullOrWhiteSpace(row[c])) { first = c; break; }
        return first == int.MaxValue ? 0 : first;
    }

    private static List<IReadOnlyList<IReadOnlyList<string>>> OpenXmlSheets(string path)
    {
        using var wb = OpenWorkbook(path);
        return wb.Worksheets.Select(ws => (IReadOnlyList<IReadOnlyList<string>>)SheetGrid(ws)).ToList();
    }

    private static List<IReadOnlyList<string>> SheetGrid(IXLWorksheet worksheet)
    {
        var used = worksheet.RangeUsed();
        if (used is null) return new List<IReadOnlyList<string>>();

        var width = used.ColumnCount();
        return used.Rows()
            .Select(row => (IReadOnlyList<string>)Enumerable.Range(1, width).Select(i => CellText(row.Cell(i))).ToList())
            .ToList();
    }

    /// <summary>
    /// Opens the workbook, turning "this is not a workbook" into a failure the
    /// caller can show the user. Left to itself ClosedXML throws
    /// FileFormatException or ArgumentException, neither of which the
    /// application handles - they would take the window down.
    /// </summary>
    private static XLWorkbook OpenWorkbook(string path)
    {
        try
        {
            return new XLWorkbook(path);
        }
        catch (Exception ex) when (ex is not (IOException or UnauthorizedAccessException))
        {
            throw new InvalidDataException(
                "Nie udało się otworzyć tego pliku jako skoroszytu Excela. " +
                "Plik może być uszkodzony lub zapisany w innym formacie.", ex);
        }
    }

    private static string CellText(IXLCell cell)
        => cell.IsEmpty() ? "" : cell.GetFormattedString().Trim();

    // ------------------------------------------------------------------ CSV

    private static List<PileRange> ReadCsv(string path)
    {
        var result = new List<PileRange>();
        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            var parts = line.Split(FieldSeparator(line), StringSplitOptions.None);
            if (parts.Length < 4) continue;
            var range = TryBuildRange(
                parts[0], parts[1], parts[2], parts[3],
                parts.Length > 4 ? parts[4] : "Brak");
            if (range is not null) result.Add(range);
        }
        return result;
    }

    /// <summary>
    /// Picks the separator for one line. A semicolon or a tab wins over a comma,
    /// because Excel exports on a Polish machine separate fields with semicolons
    /// and write decimals with commas - splitting such a line on commas as well
    /// would tear "0,4" into two fields and lose the row.
    /// </summary>
    private static char[] FieldSeparator(string line)
    {
        if (line.Contains(';')) return new[] { ';' };
        if (line.Contains('\t')) return new[] { '\t' };
        return new[] { ',' };
    }

    // ------------------------------------------------------------------ PDF

    /// <summary>
    /// Groups PDF words into visual rows by their vertical position, then reads
    /// the first five tokens of each row. Uses word coordinates rather than the
    /// raw text stream, so columns that render without separating spaces
    /// (e.g. "0.4" + "8") are still kept apart.
    /// </summary>
    private static List<PileRange> ReadPdf(string path)
    {
        var result = new List<PileRange>();
        using var doc = PdfDocument.Open(path);

        foreach (var page in doc.GetPages())
        {
            foreach (var line in GroupIntoLines(page.GetWords()))
            {
                if (line.Count < 4) continue;
                var t = line.Select(w => w.Text.Trim()).ToList();
                var range = TryBuildRange(t[0], t[1], t[2], t[3], t.Count > 4 ? t[4] : "Brak");
                if (range is not null) result.Add(range);
            }
        }

        return result;
    }

    /// <summary>
    /// Groups words into visual rows by clustering their baselines.
    ///
    /// Rounding each baseline into a fixed bucket instead would split a row
    /// whenever its words straddle a bucket edge - baselines of 99.9 and 102.4
    /// are plainly the same row, but land in buckets 25 and 26. Clustering
    /// compares each word to the row being built, so only a real gap starts a
    /// new one.
    /// </summary>
    private static IEnumerable<List<Word>> GroupIntoLines(IEnumerable<Word> words)
    {
        const double tolerance = 4.0; // points; rows in these tables are ~11 pt apart

        var ordered = words
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .OrderByDescending(w => w.BoundingBox.Bottom)
            .ToList();

        var lines = new List<List<Word>>();
        var current = new List<Word>();
        var baseline = 0.0;

        foreach (var word in ordered)
        {
            if (current.Count == 0)
            {
                baseline = word.BoundingBox.Bottom;
            }
            else if (Math.Abs(word.BoundingBox.Bottom - baseline) > tolerance)
            {
                lines.Add(current);
                current = new List<Word>();
                baseline = word.BoundingBox.Bottom;
            }

            current.Add(word);
        }

        if (current.Count > 0) lines.Add(current);

        return lines.Select(line => line.OrderBy(w => w.BoundingBox.Left).ToList());
    }

    // --------------------------------------------------------------- parsing

    private static PileRange? TryBuildRange(string from, string to, string diameter, string length, string reinforcement)
    {
        if (!TryNumber(from, out var f) || !TryNumber(to, out var t)) return null;
        if (!TryNumber(diameter, out var d) || !TryNumber(length, out var l)) return null;

        var fromNo = (int)Math.Round(f);
        var toNo = (int)Math.Round(t);

        // Guard against header rows and junk lines that happen to parse as numbers.
        if (fromNo <= 0 || toNo < fromNo) return null;
        if (d <= 0 || d > 5) return null;      // diameter in metres
        if (l <= 0 || l > 100) return null;    // length in metres

        return new PileRange
        {
            From = fromNo,
            To = toNo,
            Diameter = d,
            Length = l,
            Reinforcement = string.IsNullOrWhiteSpace(reinforcement) ? "Brak" : reinforcement.Trim()
        };
    }

    /// <summary>Parses a number written with either a dot or a comma decimal separator.</summary>
    private static bool TryNumber(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var cleaned = text.Trim().Replace(" ", "").Replace('\u00A0', ' ').Trim().Replace(',', '.');
        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
