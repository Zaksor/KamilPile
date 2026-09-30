namespace MetrykiPali.Model;

public static class PileMath
{
    /// <summary>Theoretical shaft volume of a pile, m3.</summary>
    public static double TheoreticalVolume(double diameter, double length)
        => Math.PI * diameter * diameter / 4.0 * length;

    /// <summary>Concrete placed, m3, rounded the way the metryka reports it.</summary>
    public static double Concrete(double diameter, double length, double factor)
        => Math.Round(TheoreticalVolume(diameter, length) * factor, 2, MidpointRounding.AwayFromZero);
}

/// <summary>Turns the ranges from the source table into individual piles.</summary>
public static class PileSchedule
{
    public static List<Pile> Expand(IEnumerable<PileRange> ranges, MetrykaSettings settings)
    {
        var piles = new List<Pile>();
        foreach (var r in ranges)
        {
            for (var no = r.From; no <= r.To; no++)
            {
                piles.Add(new Pile
                {
                    Number = no,
                    Diameter = r.Diameter,
                    DesignLength = r.Length,
                    ActualLength = r.Length,
                    Concrete = PileMath.Concrete(r.Diameter, r.Length, settings.ConcreteFactor),
                    ConcretePlant = settings.Betoniarnia,
                    Reinforcement = r.Reinforcement
                });
            }
        }
        return piles;
    }

    /// <summary>
    /// Pile numbers that more than one row of the schedule claims. The journal
    /// keys piles by number, so a schedule like that cannot be loaded.
    /// </summary>
    public static List<int> DuplicateNumbers(IEnumerable<PileRange> ranges) => ranges
        .SelectMany(r => Enumerable.Range(r.From, r.Count))
        .GroupBy(n => n)
        .Where(g => g.Count() > 1)
        .Select(g => g.Key)
        .OrderBy(n => n)
        .ToList();

    /// <summary>
    /// Numbers between the first and the last pile that no row covers. Often
    /// deliberate (piles dropped from the design), but worth a look.
    /// </summary>
    public static List<int> MissingNumbers(IEnumerable<PileRange> ranges)
    {
        var numbers = ranges.SelectMany(r => Enumerable.Range(r.From, r.Count)).ToHashSet();
        if (numbers.Count == 0) return new List<int>();

        return Enumerable.Range(numbers.Min(), numbers.Max() - numbers.Min() + 1)
            .Where(n => !numbers.Contains(n))
            .ToList();
    }

    /// <summary>
    /// Carries pour dates from a previous pile list onto a freshly loaded one,
    /// matching by pile number. Reloading a corrected schedule must not discard
    /// the journal - it can represent weeks of site records. The day's concrete
    /// coefficient comes along with the date, and the volume is recomputed with it.
    /// </summary>
    public static int CarryOverDates(IEnumerable<Pile> previous, IEnumerable<Pile> fresh)
    {
        var byNumber = previous.ToDictionary(p => p.Number);
        var kept = 0;

        foreach (var pile in fresh)
        {
            if (!byNumber.TryGetValue(pile.Number, out var old) || old.Executed is null) continue;
            pile.Executed = old.Executed;
            pile.ConcreteFactor = old.ConcreteFactor;
            if (old.ConcreteFactor is { } factor)
                pile.Concrete = PileMath.Concrete(pile.Diameter, pile.ActualLength, factor);
            kept++;
        }

        return kept;
    }
}
