using System.Globalization;
using System.Text;

using MetrykiPali.Model;

namespace MetrykiPali.Services;

/// <summary>
/// Reads a designer's summary table ("TABELA ZESTAWCZA PALOWANIA",
/// "ZESTAWIENIE PALI") by its headers rather than by column position.
///
/// These tables come from different designers and differ in almost everything:
/// 10 to 29 columns, in varying order; the pile numbers spread over three cells
/// ("1 | - | 19", "1 | ÷ | 28"); a single pile written in the middle cell or in
/// the first; the diameter in centimetres; reinforcement as "-", blank, "Z1" or
/// "IPE160"; totals and lookup tables (steel section masses) below the data.
/// What they share is the header text, so that is what this goes by:
///
///   NR PALI / NUMERY PALI       the pile numbers, up to the next header column
///   ŚREDNICA                    with its unit, [cm] / [mm] / [m], from the rows below
///   DŁUGOŚĆ … PAL…              the length of one pile - not ŁĄCZNA (total) and
///                               not DŁUGOŚĆ ZBROJENIA / [Lz] (the cage)
///   ZBROJENIE / RODZAJ ZBROJENIA  optional; "-" or blank means none
///
/// A row counts as a pile row only if its number cells hold one or two whole
/// numbers and its diameter and length parse, which is what leaves the totals
/// and the lookup tables out.
/// </summary>
internal static class DesignerTable
{
    /// <summary>How far down a sheet the header row is looked for.</summary>
    private const int HeaderSearchRows = 40;

    /// <summary>How many rows under the header can still be header (units, sub-captions).</summary>
    private const int MaxHeaderDepth = 5;

    /// <summary>
    /// The ranges in <paramref name="grid"/>, or null when it has no pile-number
    /// header with a diameter and a length column - it is then not this kind of
    /// table, and the caller reads it by position.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// A table of steel soldier columns, which cannot give a metryka pali.
    /// </exception>
    public static List<PileRange>? TryRead(IReadOnlyList<IReadOnlyList<string>> grid)
    {
        var (headerRow, numberCol) = FindNumberHeader(grid);
        if (headerRow < 0) return null;

        var width = grid.Max(r => r.Count);
        var numberEnd = NumberSpanEnd(grid[headerRow], numberCol);
        var firstData = FirstDataRow(grid, headerRow, numberCol, numberEnd);

        // A column's header is everything above the data: caption, sub-caption, unit.
        var headers = Enumerable.Range(0, width)
            .Select(c => Normalise(string.Join(" ", Enumerable.Range(headerRow, firstData - headerRow).Select(r => Cell(grid, r, c)))))
            .ToList();

        var diameterCol = headers.FindIndex(h => h.Contains("SREDNICA"));
        var lengthCol = headers.FindIndex(h => h.Contains("DLUGOSC") && h.Contains("PAL") && !h.Contains("LACZN") && !h.Contains("ZBROJ"));
        var reinforcementCol = headers.FindIndex(IsReinforcementHeader);

        // Only "Numer pala" over bare columns: the plain five-column layout, which
        // the positional reading handles.
        if (diameterCol < 0 || lengthCol < 0) return null;

        var diameterScale = DiameterScale(headers[diameterCol]);
        var result = new List<PileRange>();

        for (var r = firstData; r < grid.Count; r++)
        {
            var numbers = WholeNumbers(grid, r, numberCol, numberEnd);
            if (numbers.Count is 0 or > 2) continue;

            if (!TryNumber(Cell(grid, r, diameterCol), out var diameter)) continue;
            if (!TryNumber(Cell(grid, r, lengthCol), out var length)) continue;

            // A column with no unit printed: anything over 5 cannot be metres.
            var scale = diameterScale ?? (diameter > 5 ? 0.01 : 1);
            diameter = Math.Round(diameter * scale, 4);

            var from = numbers[0];
            var to = numbers.Count == 2 ? numbers[1] : numbers[0];
            if (from <= 0 || to < from) continue;
            if (diameter <= 0 || diameter > 5 || length <= 0 || length > 100) continue;

            result.Add(new PileRange
            {
                From = from,
                To = to,
                Diameter = diameter,
                Length = length,
                Reinforcement = Reinforcement(reinforcementCol < 0 ? "" : Cell(grid, r, reinforcementCol))
            });
        }

        return result;
    }

    // --------------------------------------------------------------- header

    private static (int Row, int Col) FindNumberHeader(IReadOnlyList<IReadOnlyList<string>> grid)
    {
        for (var r = 0; r < Math.Min(grid.Count, HeaderSearchRows); r++)
        {
            for (var c = 0; c < grid[r].Count; c++)
            {
                var text = Normalise(grid[r][c]);
                if (text.StartsWith("NUMERY SLUP") || text.StartsWith("NR SLUP"))
                    throw new InvalidDataException(
                        "To jest zestawienie słupów obudowy (kształtowniki stalowe, np. IPE), a nie pali. " +
                        "Nie ma w nim średnicy, więc nie da się z niego zrobić metryk pali.");

                if (text.StartsWith("NR PAL") || text.StartsWith("NUMER PAL") || text.StartsWith("NUMERY PAL"))
                    return (r, c);
            }
        }

        return (-1, -1);
    }

    /// <summary>The first row below the header whose number cells hold a pile number.</summary>
    private static int FirstDataRow(IReadOnlyList<IReadOnlyList<string>> grid, int headerRow, int numberCol, int numberEnd)
    {
        for (var r = headerRow + 1; r < Math.Min(grid.Count, headerRow + 1 + MaxHeaderDepth + 2); r++)
            if (WholeNumbers(grid, r, numberCol, numberEnd).Count > 0)
                return r;

        return Math.Min(grid.Count, headerRow + 1);
    }

    /// <summary>
    /// The pile numbers take the header's column and the unlabelled ones after
    /// it in the header row (a merged "NR PALI" over "od | - | do"), up to three.
    /// </summary>
    private static int NumberSpanEnd(IReadOnlyList<string> headerRow, int numberCol)
    {
        var end = numberCol;
        while (end + 1 < headerRow.Count && end - numberCol < 2 && Normalise(headerRow[end + 1]).Length == 0) end++;
        return end;
    }

    private static bool IsReinforcementHeader(string header)
        => (header.StartsWith("ZBROJENIE") || header.StartsWith("RODZAJ ZBROJENIA"))
           && !header.Contains("GORY") && !header.Contains("SPODU") && !header.Contains("DLUGOSC") && !header.Contains("MASA");

    /// <summary>Metres per unit of the diameter column, or null when it states none.</summary>
    private static double? DiameterScale(string header)
    {
        if (header.Contains("[CM]")) return 0.01;
        if (header.Contains("[MM]")) return 0.001;
        if (header.Contains("[M]")) return 1;
        return null;
    }

    // ----------------------------------------------------------------- rows

    private static List<int> WholeNumbers(IReadOnlyList<IReadOnlyList<string>> grid, int row, int from, int to)
    {
        var numbers = new List<int>();
        for (var c = from; c <= to; c++)
        {
            if (!TryNumber(Cell(grid, row, c), out var value)) continue;   // "-", "÷", blank
            if (value != Math.Floor(value)) return new List<int>();      // not a pile number
            numbers.Add((int)value);
        }
        return numbers;
    }

    private static string Reinforcement(string text)
    {
        var t = text.Trim();
        return t.Length == 0 || t is "-" or "–" or "—" ? "Brak" : t;
    }

    private static string Cell(IReadOnlyList<IReadOnlyList<string>> grid, int row, int col)
        => row < grid.Count && col < grid[row].Count ? grid[row][col] ?? "" : "";

    // ----------------------------------------------------------------- text

    /// <summary>Upper case, Polish letters folded to plain ones, spaces collapsed.</summary>
    internal static string Normalise(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var decomposed = text.ToUpperInvariant().Replace('Ł', 'L').Normalize(NormalizationForm.FormD);
        var plain = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                plain.Append(char.IsWhiteSpace(ch) ? ' ' : ch);

        return string.Join(' ', plain.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Parses a number written with either a dot or a comma decimal separator.</summary>
    internal static bool TryNumber(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var cleaned = text.Trim().Replace(" ", "").Replace(" ", "").Replace(',', '.');
        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
