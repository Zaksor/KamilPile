namespace MetrykiPali.Tests;

/// <summary>
/// Designers' summary tables ("TABELA ZESTAWCZA PALOWANIA", "ZESTAWIENIE PALI"),
/// read by their headers. The fixtures are synthetic but copy the layouts of
/// real project files: see the comments in each case for what makes it awkward.
/// Every expected figure was checked against the fixture's own totals row.
/// </summary>
public class DesignerTableTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static string Describe(IEnumerable<PileRange> ranges)
        => string.Join("; ", ranges.Select(r => $"{r.From}-{r.To} {r.Diameter} {r.Length} {r.Reinforcement}"));

    /// <summary>
    /// .xls; "od | - | do"; a single pile in the middle cell; diameter in cm;
    /// "-" and blank for no cage; totals and a steel-section list below.
    /// </summary>
    [Fact]
    public void Reads_an_xls_table_with_dashes_and_the_diameter_in_centimetres()
    {
        var ranges = Reader.Read(Fixture("zestawienie-myslniki.xls"));

        Assert.Equal(
            "1-12 0.36 4 Brak; 13-15 0.36 5.5 Z1; 16-16 0.36 6.5 Brak; 17-20 0.36 4.5 Brak; 22-23 0.36 5 Brak",
            Describe(ranges).Replace(',', '.'));
    }

    /// <summary>
    /// "od | ÷ | do"; a single pile in the first cell; Z1 / Z2 / IPE220; a cage
    /// length [Lz] and a second "ZBROJENIE" table that must not be taken for the
    /// pile's; a stray number above the header; a SUMA row.
    /// </summary>
    [Fact]
    public void Reads_a_table_with_division_signs_and_cage_columns_beside_the_pile_columns()
    {
        var ranges = Reader.Read(Fixture("zestawienie-daszki.xls"));

        Assert.Equal("1-10 0.6 13 Z1; 11-11 0.6 14 Z2; 12-14 0.6 15 IPE220", Describe(ranges).Replace(',', '.'));
        Assert.Equal(189, ranges.Sum(r => r.Count * r.Length));    // the table's own SUMA
    }

    /// <summary>
    /// .xlsx; the length of one pile before the total; reinforcement as TYP over
    /// GATUNEK with a steel section on a single pile.
    /// </summary>
    [Fact]
    public void Reads_the_length_of_one_pile_not_the_total_whatever_the_column_order()
    {
        var ranges = Reader.Read(Fixture("zestawienie-kolejnosc.xlsx"));

        Assert.Equal("1-2 0.6 7 Brak; 3-3 0.6 9 IPE160; 4-8 0.6 5.5 Brak", Describe(ranges).Replace(',', '.'));
    }

    [Fact]
    public void A_table_of_steel_soldier_columns_is_refused_with_the_reason()
    {
        var thrown = Record.Exception(() => Reader.Read(Fixture("zestawienie-slupy.xls")));

        Assert.IsType<InvalidDataException>(thrown);
        Assert.Contains("słupów obudowy", thrown!.Message);
    }

    [Fact]
    public void The_familiar_five_column_tables_still_read_as_before()
    {
        Assert.Equal(60, Reader.Read(Fixture("tabelka-testowa.xlsx")).Sum(r => r.Count));
        Assert.Equal(3, Reader.Read(Fixture("tabelka-smieci.csv")).Count);
    }

    [Fact]
    public void Concrete_is_worked_out_from_the_diameter_in_metres()
    {
        var piles = PileSchedule.Expand(Reader.Read(Fixture("zestawienie-myslniki.xls")), new MetrykaSettings { ConcreteFactor = 1.30 });

        Assert.Equal(PileMath.Concrete(0.36, 4, 1.30), piles[0].Concrete);
        Assert.True(piles[0].Concrete < 1);    // 36 taken as metres would give ~5300 m3
    }

    // ------------------------------------------------------ numbering checks

    [Fact]
    public void Gaps_in_the_numbering_are_found()
    {
        var ranges = Reader.Read(Fixture("zestawienie-myslniki.xls"));

        Assert.Equal(new[] { 21 }, PileSchedule.MissingNumbers(ranges));
        Assert.Empty(PileSchedule.DuplicateNumbers(ranges));
    }

    [Fact]
    public void Numbers_claimed_by_two_rows_are_found()
    {
        var ranges = new[]
        {
            new PileRange { From = 1, To = 10 },
            new PileRange { From = 8, To = 12 },
            new PileRange { From = 20, To = 20 },
            new PileRange { From = 20, To = 20 }
        };

        Assert.Equal(new[] { 8, 9, 10, 20 }, PileSchedule.DuplicateNumbers(ranges));
    }
}
