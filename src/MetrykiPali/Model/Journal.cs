namespace MetrykiPali.Model;

/// <summary>
/// What adding a set of pile numbers to a day would do, worked out before
/// anything changes so the presenter can ask the user about the awkward parts
/// (numbers that are not in the schedule, piles already logged on another day)
/// and then apply exactly what was agreed.
/// </summary>
/// <summary>A logged day with piles whose metryka has not been written yet.</summary>
public sealed record MissingDay(DateTime Date, IReadOnlyList<int> Numbers, int DayCount);

/// <summary>Everything that still has no metryka.</summary>
public sealed record MissingMetryki(IReadOnlyList<int> Undated, IReadOnlyList<MissingDay> Days)
{
    public bool None => Undated.Count == 0 && Days.Count == 0;
}

public sealed record JournalPlan(
    DateTime Date,
    IReadOnlyList<int> Known,
    IReadOnlyList<int> Unknown,
    IReadOnlyList<int> Moved)
{
    public bool HasAnythingToDo => Known.Count > 0;
}

/// <summary>
/// The work journal: which piles were poured on which day.
///
/// The date lives on the pile itself (<see cref="Pile.Executed"/>), so there is
/// one source of truth - no second list that could drift out of step with it.
/// </summary>
public sealed class Journal
{
    private readonly List<Pile> _piles;

    public Journal(List<Pile> piles) => _piles = piles;

    public IReadOnlyList<Pile> Piles => _piles;
    public int Total => _piles.Count;
    public int Assigned => _piles.Count(p => p.Executed is not null);
    public int Outstanding => Total - Assigned;

    /// <summary>Works out what adding these numbers to this day would mean.</summary>
    public JournalPlan Plan(IEnumerable<int> numbers, DateTime date)
    {
        date = date.Date;
        var byNumber = _piles.ToDictionary(p => p.Number);
        var known = new List<int>();
        var unknown = new List<int>();
        var moved = new List<int>();

        foreach (var number in numbers.Distinct().OrderBy(n => n))
        {
            if (!byNumber.TryGetValue(number, out var pile)) { unknown.Add(number); continue; }

            known.Add(number);
            if (pile.Executed is not null && pile.Executed.Value.Date != date) moved.Add(number);
        }

        return new JournalPlan(date, known, unknown, moved);
    }

    /// <summary>
    /// Applies a plan and returns how many piles were logged. The piles take
    /// the day's coefficient: the one it already has if piles were logged to it
    /// before, otherwise <paramref name="factor"/> - the value in the field now.
    /// A day therefore never mixes two coefficients.
    /// </summary>
    public int Apply(JournalPlan plan, double factor)
    {
        var dayFactor = FactorOf(plan.Date) ?? factor;
        var byNumber = _piles.ToDictionary(p => p.Number);
        var applied = 0;

        foreach (var number in plan.Known)
        {
            if (!byNumber.TryGetValue(number, out var pile)) continue;
            if (pile.Executed?.Date != plan.Date) pile.MetrykaGenerated = null;   // a new day: no metryka yet
            pile.Executed = plan.Date;
            pile.ConcreteFactor = dayFactor;
            RecalculateConcrete(pile, factor);
            applied++;
        }

        return applied;
    }

    /// <summary>
    /// Returns a day's piles to the pool of piles with no date. They lose the
    /// day's coefficient and follow <paramref name="factor"/> again.
    /// </summary>
    public int RemoveDay(DateTime date, double factor)
    {
        date = date.Date;
        var cleared = 0;

        foreach (var pile in _piles.Where(p => p.Executed?.Date == date))
        {
            pile.Executed = null;
            pile.ConcreteFactor = null;
            pile.MetrykaGenerated = null;
            RecalculateConcrete(pile, factor);
            cleared++;
        }

        return cleared;
    }

    /// <summary>The coefficient a logged day was given, or null for a day with no piles.</summary>
    public double? FactorOf(DateTime date)
        => _piles.FirstOrDefault(p => p.Executed?.Date == date.Date)?.ConcreteFactor;

    /// <summary>Changes one day's coefficient and recomputes that day's piles only.</summary>
    public int SetDayFactor(DateTime date, double factor)
    {
        var changed = 0;

        foreach (var pile in _piles.Where(p => p.Executed?.Date == date.Date))
        {
            if (pile.ConcreteFactor != factor) pile.MetrykaGenerated = null;   // the printed volume is out of date
            pile.ConcreteFactor = factor;
            RecalculateConcrete(pile, factor);
            changed++;
        }

        return changed;
    }

    // -------------------------------------------------------------- metryki

    /// <summary>Records that these days' metryki were written.</summary>
    public void MarkGenerated(IEnumerable<WorkDay> days, DateTime when)
    {
        foreach (var pile in days.SelectMany(d => d.Piles)) pile.MetrykaGenerated = when;
    }

    /// <summary>A pile's metryka no longer matches it (length or diameter corrected).</summary>
    public static void Invalidate(Pile pile) => pile.MetrykaGenerated = null;

    /// <summary>
    /// What still has no metryka: piles with no date at all, and logged days
    /// with piles not written since they were last changed.
    /// </summary>
    public MissingMetryki Missing() => new(
        _piles.Where(p => p.Executed is null).Select(p => p.Number).OrderBy(n => n).ToList(),
        Days()
            .Select(d => new MissingDay(d.Date, d.Piles.Where(p => p.MetrykaGenerated is null).Select(p => p.Number).ToList(), d.Piles.Count))
            .Where(d => d.Numbers.Count > 0)
            .ToList());

    /// <summary>
    /// Gives logged piles that have no coefficient of their own - from a file
    /// written before days had one - the project's coefficient. Their stored
    /// volumes were computed with it, so nothing on paper changes.
    /// </summary>
    public int PinMissingFactors(double factor)
    {
        var pinned = 0;

        foreach (var pile in _piles.Where(p => p.Executed is not null && p.ConcreteFactor is null))
        {
            pile.ConcreteFactor = factor;
            pinned++;
        }

        return pinned;
    }

    /// <summary>The logged days, in date order, each with its piles in number order.</summary>
    public List<WorkDay> Days() => _piles
        .Where(p => p.Executed is not null)
        .GroupBy(p => p.Executed!.Value.Date)
        .OrderBy(g => g.Key)
        .Select(g => new WorkDay { Date = g.Key, Piles = g.OrderBy(p => p.Number).ToList() })
        .ToList();

    /// <summary>The same days rendered for the journal grid, oldest or newest first.</summary>
    public List<JournalEntry> Entries(int pilesPerPage, bool newestFirst = false)
    {
        var perPage = Math.Max(1, pilesPerPage);

        var entries = Days().Select(day => new JournalEntry
        {
            Data = day.Date,
            Pale = PileNumbers.Format(day.Piles.Select(p => p.Number)),
            Ilosc = day.Piles.Count,
            Beton = Math.Round(day.Piles.Sum(p => p.Concrete), 2),
            Wsp = day.Piles[0].ConcreteFactor ?? 0,
            Strony = (int)Math.Ceiling(day.Piles.Count / (double)perPage),
            Metryki = GeneratedState(day.Piles)
        }).ToList();

        if (newestFirst) entries.Reverse();
        return entries;
    }

    /// <summary>"12.09.2026" when every pile was written then (or later), "nie", or "częściowo".</summary>
    private static string GeneratedState(IReadOnlyList<Pile> piles)
    {
        var done = piles.Where(p => p.MetrykaGenerated is not null).ToList();
        if (done.Count == 0) return "nie";
        if (done.Count < piles.Count) return "częściowo";
        return done.Min(p => p.MetrykaGenerated!.Value).ToString("dd.MM.yyyy");
    }

    /// <summary>
    /// Recomputes the piles that follow the coefficient field - the outstanding
    /// ones - after it changes. Logged days keep the coefficient they were given.
    /// </summary>
    public void RecalculateConcrete(double factor)
    {
        foreach (var pile in _piles.Where(p => p.ConcreteFactor is null))
            RecalculateConcrete(pile, factor);
    }

    /// <summary>
    /// Recomputes one pile, e.g. after its length or diameter was corrected, with
    /// its own coefficient if it has one and <paramref name="factor"/> otherwise.
    /// </summary>
    public void RecalculateConcrete(Pile pile, double factor)
        => pile.Concrete = PileMath.Concrete(pile.Diameter, pile.ActualLength, pile.ConcreteFactor ?? factor);

    public void SetConcretePlant(string plant)
    {
        foreach (var pile in _piles) pile.ConcretePlant = plant;
    }
}
