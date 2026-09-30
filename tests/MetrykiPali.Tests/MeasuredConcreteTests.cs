namespace MetrykiPali.Tests;

/// <summary>
/// A day's concrete taken from what was actually used (the delivery notes) and
/// shared among its piles by volume, instead of volume × coefficient.
/// </summary>
public class MeasuredConcreteTests
{
    private static readonly DateTime D12 = new(2022, 9, 12);
    private static readonly DateTime D13 = new(2022, 9, 13);

    // ------------------------------------------------------------- sharing

    [Fact]
    public void Piles_of_one_size_share_the_total_equally()
    {
        var shares = PileMath.Distribute(12.00, Enumerable.Repeat((0.4, 7.0), 12).ToList());

        Assert.All(shares, s => Assert.Equal(1.00, s));
    }

    [Fact]
    public void Shares_follow_the_volume_so_a_longer_or_wider_pile_gets_more()
    {
        var shares = PileMath.Distribute(10.00, new[] { (0.4, 5.0), (0.4, 10.0), (0.8, 5.0) });

        // volumes 1 : 2 : 4
        Assert.Equal(new[] { 1.43, 2.86, 5.71 }, shares);
    }

    [Theory]
    [InlineData(10.00, 3)]
    [InlineData(14.87, 7)]
    [InlineData(22.08, 18)]
    [InlineData(0.05, 12)]
    [InlineData(123.45, 97)]
    public void Rounded_shares_add_up_to_the_total_to_the_cent(double total, int count)
    {
        var piles = Enumerable.Range(0, count).Select(i => (0.4 + i % 3 * 0.1, 6.0 + i % 5)).ToList();

        var shares = PileMath.Distribute(total, piles);

        Assert.Equal(total, Math.Round(shares.Sum(), 2));
        Assert.All(shares, s => Assert.Equal(s, Math.Round(s, 2)));
    }

    [Fact]
    public void Plain_rounding_would_lose_a_cent_that_the_sharing_keeps()
    {
        // 10.00 over three equal piles: 3.33 × 3 = 9.99.
        var shares = PileMath.Distribute(10.00, Enumerable.Repeat((0.4, 7.0), 3).ToList());

        Assert.Equal(new[] { 3.34, 3.33, 3.33 }, shares);
    }

    // ------------------------------------------------------------- journal

    private static Journal WithPiles(params (double D, double L)[] piles) => new(piles
        .Select((p, i) => new Pile { Number = i + 1, Diameter = p.D, DesignLength = p.L, ActualLength = p.L })
        .ToList());

    [Fact]
    public void A_day_given_its_concrete_used_shares_it_and_shows_what_that_comes_to()
    {
        var journal = WithPiles((0.4, 7), (0.4, 7), (0.4, 7), (0.4, 7));
        journal.Apply(journal.Plan(new[] { 1, 2, 3, 4 }, D12), 1.30);

        journal.SetDayConcreteUsed(D12, 4.80, 1.30);

        Assert.All(journal.Piles, p => Assert.Equal(1.20, p.Concrete));
        var entry = Assert.Single(journal.Entries(12));
        Assert.Equal(4.80, entry.Zuzyto);
        Assert.Equal(4.80, entry.Beton);
        Assert.Equal(Math.Round(4.80 / (4 * PileMath.TheoreticalVolume(0.4, 7)), 2), entry.Wsp);
    }

    [Fact]
    public void Piles_added_to_such_a_day_share_the_same_total()
    {
        var journal = WithPiles((0.4, 7), (0.4, 7), (0.4, 7), (0.4, 7));
        journal.Apply(journal.Plan(new[] { 1, 2 }, D12), 1.30);
        journal.SetDayConcreteUsed(D12, 4.00, 1.30);

        journal.Apply(journal.Plan(new[] { 3, 4 }, D12), 1.30);

        Assert.All(journal.Piles, p => Assert.Equal(1.00, p.Concrete));
    }

    [Fact]
    public void Moving_a_pile_off_such_a_day_shares_its_total_among_those_left()
    {
        var journal = WithPiles((0.4, 7), (0.4, 7), (0.4, 7), (0.4, 7));
        journal.Apply(journal.Plan(new[] { 1, 2, 3, 4 }, D12), 1.30);
        journal.SetDayConcreteUsed(D12, 4.00, 1.30);

        journal.Apply(journal.Plan(new[] { 4 }, D13), 1.30);

        Assert.Equal(new[] { 1.33, 1.34, 1.33 }.OrderBy(x => x), journal.Piles.Take(3).Select(p => p.Concrete).OrderBy(x => x));
        Assert.Equal(4.00, Math.Round(journal.Piles.Take(3).Sum(p => p.Concrete), 2));
        Assert.Null(journal.Piles[3].DayConcreteUsed);
        Assert.Equal(PileMath.Concrete(0.4, 7, 1.30), journal.Piles[3].Concrete);
    }

    [Fact]
    public void Correcting_a_length_on_such_a_day_reshares_the_whole_day()
    {
        var journal = WithPiles((0.4, 5), (0.4, 5));
        journal.Apply(journal.Plan(new[] { 1, 2 }, D12), 1.30);
        journal.SetDayConcreteUsed(D12, 3.00, 1.30);

        journal.Piles[1].ActualLength = 10;
        journal.RecalculateConcrete(journal.Piles[1], 1.30);

        Assert.Equal(new[] { 1.00, 2.00 }, journal.Piles.Select(p => p.Concrete));
    }

    [Fact]
    public void Clearing_the_total_or_setting_a_coefficient_goes_back_to_the_coefficient()
    {
        var journal = WithPiles((0.4, 7), (0.4, 7));
        journal.Apply(journal.Plan(new[] { 1, 2 }, D12), 1.25);
        journal.SetDayConcreteUsed(D12, 5.00, 1.30);

        journal.SetDayConcreteUsed(D12, null, 1.30);
        Assert.All(journal.Piles, p => Assert.Equal(PileMath.Concrete(0.4, 7, 1.25), p.Concrete));

        journal.SetDayConcreteUsed(D12, 5.00, 1.30);
        journal.SetDayFactor(D12, 1.40);
        Assert.Null(journal.ConcreteUsedOf(D12));
        Assert.All(journal.Piles, p => Assert.Equal(PileMath.Concrete(0.4, 7, 1.40), p.Concrete));
    }

    [Fact]
    public void Resharing_that_changes_a_generated_piles_figure_marks_it_missing()
    {
        var journal = WithPiles((0.4, 7), (0.4, 7), (0.4, 7));
        journal.Apply(journal.Plan(new[] { 1, 2 }, D12), 1.30);
        journal.SetDayConcreteUsed(D12, 3.00, 1.30);
        journal.MarkGenerated(journal.Days(), D13);

        journal.Apply(journal.Plan(new[] { 3 }, D12), 1.30);   // 3.00 now over three piles

        Assert.All(journal.Piles, p => Assert.Null(p.MetrykaGenerated));
    }

    // ----------------------------------------------------------- presenter

    private readonly FakeMainView _view = new();
    private readonly InMemoryProjectRepository _repository = new();
    private readonly StubScheduleReader _reader = new((1, 12, 0.4, 7), (13, 24, 0.6, 10));

    public MeasuredConcreteTests()
    {
        new MainPresenter(_view, _reader, new RecordingMetrykaWriter(), _repository).Start();
        _view.SchedulePath = @"C:\budowa\tabelka.xlsx";
        _view.ClickLoadSchedule();
    }

    [Fact]
    public void With_the_concrete_used_method_the_figure_typed_with_the_piles_is_shared()
    {
        _view.ChangeConcreteMode(ConcreteMode.Measured);

        _view.LogDay(D12, "1-12", concreteUsed: 13.20);

        Assert.All(_view.Piles.Take(12), p => Assert.Equal(1.10, p.Concrete));
        Assert.Equal(13.20, _view.Journal[0].Zuzyto);
        Assert.Null(_view.JournalConcreteUsed);          // cleared for the next day
        Assert.Contains("beton zużyty 13,20 m³", _view.StatusText);
    }

    [Fact]
    public void Left_empty_the_day_uses_the_coefficient_until_the_figure_is_entered()
    {
        _view.ChangeConcreteMode(ConcreteMode.Measured);

        _view.LogDay(D12, "1-12");
        Assert.Contains("wpisz ilość zużytego betonu", _view.StatusText);
        Assert.False(_view.Journal[0].Mierzone);

        _view.EditDayConcrete(D12, 13.20);
        Assert.Equal(13.20, _view.Journal[0].Beton);
    }

    [Fact]
    public void The_coefficient_method_ignores_a_stray_figure_in_the_field()
    {
        _view.LogDay(D12, "1-12", concreteUsed: 50);

        Assert.False(_view.Journal[0].Mierzone);
        Assert.Equal(PileMath.Concrete(0.4, 7, 1.30), _view.Piles[0].Concrete);
    }

    [Fact]
    public void Clearing_the_figure_in_the_journal_goes_back_to_the_coefficient()
    {
        _view.LogDay(D12, "1-12");
        _view.EditDayConcrete(D12, 13.20);

        _view.EditDayConcrete(D12, null);

        Assert.False(_view.Journal[0].Mierzone);
        Assert.Equal(PileMath.Concrete(0.4, 7, 1.30), _view.Piles[0].Concrete);
    }

    [Fact]
    public void A_figure_below_the_holes_own_volume_is_questioned_and_can_be_declined()
    {
        _view.LogDay(D12, "1-12");                      // 12 × 0.88 m3 theoretical
        _view.AnswerConfirm = (title, _) => title != "Sprawdź ilość betonu";

        _view.EditDayConcrete(D12, 5.00);

        Assert.Contains(_view.Questions, q => q.StartsWith("Sprawdź ilość betonu") && q.Contains("mniej niż sama objętość"));
        Assert.False(_view.Journal[0].Mierzone);
    }

    [Fact]
    public void A_sensible_figure_is_taken_without_a_question()
    {
        _view.LogDay(D12, "1-12");

        _view.EditDayConcrete(D12, 12.50);                // 1.18 × theoretical

        Assert.DoesNotContain(_view.Questions, q => q.StartsWith("Sprawdź ilość betonu"));
        Assert.Equal(12.50, _view.Journal[0].Zuzyto);
    }

    [Fact]
    public void An_absurd_figure_is_refused()
    {
        _view.LogDay(D12, "1-12");

        _view.EditDayConcrete(D12, 1_000_000);

        Assert.Contains(_view.Errors, e => e.StartsWith("Błędna ilość betonu"));
        Assert.False(_view.Journal[0].Mierzone);
    }

    [Fact]
    public void Days_can_mix_the_two_methods_and_both_survive_a_restart()
    {
        _view.LogDay(D12, "1-12");                        // coefficient
        _view.ChangeConcreteMode(ConcreteMode.Measured);
        _view.LogDay(D13, "13-24", concreteUsed: 45.00);  // used

        var next = new FakeMainView();
        new MainPresenter(next, _reader, new RecordingMetrykaWriter(), _repository).Start();

        Assert.Equal(ConcreteMode.Measured, next.ConcreteMode);
        Assert.False(next.Journal[0].Mierzone);
        Assert.Equal(1.30, next.Journal[0].Wsp);
        Assert.Equal(45.00, next.Journal[1].Zuzyto);
        Assert.Equal(45.00, Math.Round(next.Piles.Skip(12).Sum(p => p.Concrete), 2));
    }

    [Fact]
    public void Reloading_the_schedule_keeps_the_days_total_and_shares_it_again()
    {
        _view.LogDay(D12, "1-12");
        _view.EditDayConcrete(D12, 13.20);

        _view.SchedulePath = @"C:\budowa\tabelka-poprawiona.xlsx";
        _view.ClickLoadSchedule();

        Assert.Equal(13.20, _view.Journal[0].Zuzyto);
        Assert.Equal(13.20, Math.Round(_view.Piles.Take(12).Sum(p => p.Concrete), 2));
    }
}
