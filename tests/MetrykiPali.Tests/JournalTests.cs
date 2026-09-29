namespace MetrykiPali.Tests;

/// <summary>
/// The journal decides what a change would do before doing it, so the presenter
/// can put the awkward cases to the user. These cover that split directly.
/// </summary>
public class JournalTests
{
    private static readonly DateTime D12 = new(2022, 9, 12);
    private static readonly DateTime D13 = new(2022, 9, 13);

    private static Journal WithPiles(int count) => new(Enumerable.Range(1, count)
        .Select(n => new Pile { Number = n, Diameter = 0.4, DesignLength = 9, ActualLength = 9, Concrete = 1.47 })
        .ToList());

    [Fact]
    public void Plan_separates_known_from_unknown_numbers()
    {
        var plan = WithPiles(10).Plan(new[] { 1, 2, 99 }, D12);

        Assert.Equal(new[] { 1, 2 }, plan.Known);
        Assert.Equal(new[] { 99 }, plan.Unknown);
        Assert.Empty(plan.Moved);
    }

    [Fact]
    public void Plan_flags_piles_already_logged_on_another_day()
    {
        var journal = WithPiles(10);
        journal.Apply(journal.Plan(new[] { 1, 2 }, D12), 1.30);

        var plan = journal.Plan(new[] { 2, 3 }, D13);

        Assert.Equal(new[] { 2 }, plan.Moved);
        Assert.Equal(new[] { 2, 3 }, plan.Known);
    }

    [Fact]
    public void Plan_does_not_flag_a_pile_logged_on_the_same_day()
    {
        var journal = WithPiles(10);
        journal.Apply(journal.Plan(new[] { 1 }, D12), 1.30);

        Assert.Empty(journal.Plan(new[] { 1 }, D12).Moved);
    }

    [Fact]
    public void Plan_changes_nothing_on_its_own()
    {
        var journal = WithPiles(10);

        journal.Plan(new[] { 1, 2, 3 }, D12);

        Assert.Equal(0, journal.Assigned);
    }

    [Fact]
    public void Plan_sorts_and_deduplicates()
    {
        var plan = WithPiles(10).Plan(new[] { 3, 1, 3, 2 }, D12);

        Assert.Equal(new[] { 1, 2, 3 }, plan.Known);
    }

    [Fact]
    public void A_plan_with_nothing_known_has_nothing_to_do()
    {
        var plan = WithPiles(10).Plan(new[] { 98, 99 }, D12);

        Assert.False(plan.HasAnythingToDo);
    }

    [Fact]
    public void Apply_logs_only_the_known_piles()
    {
        var journal = WithPiles(10);

        var applied = journal.Apply(journal.Plan(new[] { 1, 2, 99 }, D12), 1.30);

        Assert.Equal(2, applied);
        Assert.Equal(2, journal.Assigned);
        Assert.Equal(8, journal.Outstanding);
    }

    [Fact]
    public void Applying_a_move_does_not_leave_the_pile_on_both_days()
    {
        var journal = WithPiles(10);
        journal.Apply(journal.Plan(new[] { 1, 2, 3 }, D12), 1.30);
        journal.Apply(journal.Plan(new[] { 2 }, D13), 1.30);

        var days = journal.Days();

        Assert.Equal(new[] { 1, 3 }, days[0].Piles.Select(p => p.Number));
        Assert.Equal(new[] { 2 }, days[1].Piles.Select(p => p.Number));
        Assert.Equal(3, days.Sum(d => d.Piles.Count));
    }

    [Fact]
    public void RemoveDay_returns_its_piles_to_the_pool()
    {
        var journal = WithPiles(10);
        journal.Apply(journal.Plan(new[] { 1, 2, 3 }, D12), 1.30);

        var cleared = journal.RemoveDay(D12, 1.30);

        Assert.Equal(3, cleared);
        Assert.Equal(0, journal.Assigned);
        Assert.Empty(journal.Days());
    }

    [Fact]
    public void RemoveDay_leaves_other_days_alone()
    {
        var journal = WithPiles(10);
        journal.Apply(journal.Plan(new[] { 1, 2 }, D12), 1.30);
        journal.Apply(journal.Plan(new[] { 3, 4 }, D13), 1.30);

        journal.RemoveDay(D12, 1.30);

        var day = Assert.Single(journal.Days());
        Assert.Equal(D13, day.Date);
    }

    [Fact]
    public void Entries_report_pages_per_day()
    {
        var journal = WithPiles(30);
        journal.Apply(journal.Plan(Enumerable.Range(1, 18).ToArray(), D12), 1.30);

        var entry = Assert.Single(journal.Entries(12));

        Assert.Equal(18, entry.Ilosc);
        Assert.Equal(2, entry.Strony);
        Assert.Equal("1-18", entry.Pale);
        Assert.Equal(Math.Round(18 * 1.47, 2), entry.Beton);
    }

    [Fact]
    public void Recalculating_uses_the_driven_length_not_the_design_length()
    {
        var journal = WithPiles(1);
        journal.Piles[0].ActualLength = 7;

        journal.RecalculateConcrete(1.30);

        Assert.Equal(1.14, journal.Piles[0].Concrete);
    }

    [Fact]
    public void The_plant_can_be_set_for_the_whole_job()
    {
        var journal = WithPiles(5);

        journal.SetConcretePlant("Lafarge");

        Assert.All(journal.Piles, p => Assert.Equal("Lafarge", p.ConcretePlant));
    }

    [Fact]
    public void Dates_are_kept_by_day_not_by_instant()
    {
        var journal = WithPiles(3);

        journal.Apply(journal.Plan(new[] { 1 }, new DateTime(2022, 9, 12, 14, 30, 0)), 1.30);

        Assert.Equal(new DateTime(2022, 9, 12), journal.Days()[0].Date);
    }

    // ------------------------------------------------- per-day coefficient

    [Fact]
    public void A_logged_pile_takes_the_coefficient_in_force_that_day()
    {
        var journal = WithPiles(3);

        journal.Apply(journal.Plan(new[] { 1 }, D12), 1.20);

        Assert.Equal(1.20, journal.Piles[0].ConcreteFactor);
        Assert.Equal(PileMath.Concrete(0.4, 9, 1.20), journal.Piles[0].Concrete);
        Assert.Null(journal.Piles[1].ConcreteFactor);
    }

    [Fact]
    public void Changing_the_coefficient_leaves_logged_days_alone()
    {
        var journal = WithPiles(3);
        journal.Apply(journal.Plan(new[] { 1 }, D12), 1.30);

        journal.RecalculateConcrete(1.00);

        Assert.Equal(PileMath.Concrete(0.4, 9, 1.30), journal.Piles[0].Concrete);   // logged: kept
        Assert.Equal(PileMath.Concrete(0.4, 9, 1.00), journal.Piles[1].Concrete);   // outstanding: follows
    }

    [Fact]
    public void Adding_to_an_existing_day_keeps_that_days_coefficient()
    {
        var journal = WithPiles(3);
        journal.Apply(journal.Plan(new[] { 1 }, D12), 1.20);

        journal.Apply(journal.Plan(new[] { 2 }, D12), 1.40);

        Assert.All(journal.Days()[0].Piles, p => Assert.Equal(1.20, p.ConcreteFactor));
    }

    [Fact]
    public void A_moved_pile_takes_the_coefficient_of_the_day_it_moves_to()
    {
        var journal = WithPiles(3);
        journal.Apply(journal.Plan(new[] { 1 }, D12), 1.20);
        journal.Apply(journal.Plan(new[] { 2 }, D13), 1.40);

        journal.Apply(journal.Plan(new[] { 1 }, D13), 1.00);

        Assert.Equal(1.40, journal.Piles[0].ConcreteFactor);
        Assert.Equal(PileMath.Concrete(0.4, 9, 1.40), journal.Piles[0].Concrete);
    }

    [Fact]
    public void One_days_coefficient_can_be_changed_without_touching_the_others()
    {
        var journal = WithPiles(4);
        journal.Apply(journal.Plan(new[] { 1, 2 }, D12), 1.30);
        journal.Apply(journal.Plan(new[] { 3, 4 }, D13), 1.30);

        var changed = journal.SetDayFactor(D13, 1.45);

        Assert.Equal(2, changed);
        Assert.Equal(1.30, journal.FactorOf(D12));
        Assert.Equal(1.45, journal.FactorOf(D13));
        Assert.Equal(PileMath.Concrete(0.4, 9, 1.45), journal.Piles[3].Concrete);
        Assert.Equal(1.45, journal.Entries(12)[1].Wsp);
    }

    [Fact]
    public void Removing_a_day_returns_its_piles_to_the_current_coefficient()
    {
        var journal = WithPiles(2);
        journal.Apply(journal.Plan(new[] { 1 }, D12), 1.45);

        journal.RemoveDay(D12, 1.10);

        Assert.Null(journal.Piles[0].ConcreteFactor);
        Assert.Equal(PileMath.Concrete(0.4, 9, 1.10), journal.Piles[0].Concrete);
    }

    [Fact]
    public void A_corrected_length_is_recomputed_with_the_piles_own_coefficient()
    {
        var journal = WithPiles(1);
        journal.Apply(journal.Plan(new[] { 1 }, D12), 1.20);
        journal.Piles[0].ActualLength = 10;

        journal.RecalculateConcrete(journal.Piles[0], 1.50);

        Assert.Equal(PileMath.Concrete(0.4, 10, 1.20), journal.Piles[0].Concrete);
    }

    [Fact]
    public void Days_logged_before_coefficients_were_per_day_are_pinned_without_changing_volumes()
    {
        var journal = WithPiles(2);
        journal.Piles[0].Executed = D12;          // as a version 1 file leaves it
        var before = journal.Piles[0].Concrete;

        var pinned = journal.PinMissingFactors(1.30);

        Assert.Equal(1, pinned);
        Assert.Equal(1.30, journal.Piles[0].ConcreteFactor);
        Assert.Equal(before, journal.Piles[0].Concrete);
        Assert.Null(journal.Piles[1].ConcreteFactor);
    }
}
