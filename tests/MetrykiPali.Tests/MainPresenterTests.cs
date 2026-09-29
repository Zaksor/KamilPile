namespace MetrykiPali.Tests;

/// <summary>
/// The application driven end to end through its presenter: buttons pressed,
/// questions answered, results checked - with no window and no disk.
/// </summary>
public class MainPresenterTests
{
    private readonly FakeMainView _view = new();
    private readonly StubScheduleReader _reader = new((1, 12, 0.4, 7), (13, 24, 0.4, 8), (25, 36, 0.4, 9));
    private readonly RecordingMetrykaWriter _writer = new();
    private readonly InMemoryProjectRepository _repository = new();
    private readonly MainPresenter _presenter;

    private static readonly DateTime D12 = new(2022, 9, 12);
    private static readonly DateTime D13 = new(2022, 9, 13);

    public MainPresenterTests()
    {
        _presenter = new MainPresenter(_view, _reader, _writer, _repository);
        _presenter.Start();
    }

    /// <summary>Loads the stub schedule, as pressing "Wczytaj tabelkę" would.</summary>
    private void LoadSchedule(string path = @"C:\budowa\tabelka.xlsx")
    {
        _view.SchedulePath = path;
        _view.ClickLoadSchedule();
    }

    // ------------------------------------------------------------- start-up

    [Fact]
    public void Starts_empty_and_invites_the_user_to_load_a_schedule()
    {
        Assert.Empty(_view.Piles);
        Assert.False(_view.CanGenerate);
        Assert.Contains("Wczytaj tabelkę", _view.StatusText);
    }

    [Fact]
    public void Starts_by_backing_up_and_reopening_the_saved_project()
    {
        Assert.Equal(1, _repository.Backups);
        Assert.Equal(_repository.DefaultPath, _presenter.ProjectPath);
    }

    [Fact]
    public void Reopens_the_journal_saved_by_a_previous_run()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-12");

        // A second presenter over the same store is the next launch of the app.
        var next = new FakeMainView();
        new MainPresenter(next, _reader, _writer, _repository).Start();

        Assert.Equal(36, next.Piles.Count);
        Assert.Single(next.Journal);
        Assert.Equal(12, next.Journal[0].Ilosc);
        Assert.True(next.CanGenerate);
    }

    // --------------------------------------------------------------- loading

    [Fact]
    public void Loading_a_schedule_fills_the_grids()
    {
        LoadSchedule();

        Assert.Equal(3, _view.Ranges.Count);
        Assert.Equal(36, _view.Piles.Count);
        Assert.Equal(@"C:\budowa\tabelka.xlsx", _view.SourcePath);
        Assert.Contains("Wczytano 3", _view.StatusText);
    }

    [Fact]
    public void Loading_computes_the_concrete_volumes()
    {
        LoadSchedule();

        Assert.Equal(1.14, _view.Piles[0].Concrete);    // 7 m
        Assert.Equal(1.47, _view.Piles[^1].Concrete);   // 9 m
    }

    [Fact]
    public void Cancelling_the_file_dialog_changes_nothing()
    {
        _view.SchedulePath = null;
        _view.ClickLoadSchedule();

        Assert.Empty(_view.Piles);
        Assert.Empty(_view.Errors);
    }

    [Fact]
    public void A_schedule_that_cannot_be_read_is_reported_not_thrown()
    {
        var view = new FakeMainView { SchedulePath = @"C:\zly.docx" };
        var presenter = new MainPresenter(
            view, new StubScheduleReader(new InvalidDataException("Nie znaleziono zakresów")),
            _writer, new InMemoryProjectRepository());
        presenter.Start();

        view.ClickLoadSchedule();

        Assert.Single(view.Errors);
        Assert.Contains("Nie znaleziono zakresów", view.Errors[0]);
    }

    [Fact]
    public void Reloading_a_corrected_schedule_keeps_the_journal()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-12");

        LoadSchedule(@"C:\budowa\tabelka-poprawiona.xlsx");

        Assert.Single(_view.Journal);
        Assert.Equal(12, _view.Journal[0].Ilosc);
        Assert.Contains("Zachowano daty dla 12 pali", _view.StatusText);
    }

    // --------------------------------------------------------------- journal

    [Fact]
    public void Logging_a_day_puts_it_in_the_journal()
    {
        LoadSchedule();

        _view.LogDay(D12, "1-10, 17, 18");

        var day = Assert.Single(_view.Journal);
        Assert.Equal(D12, day.Data);
        Assert.Equal(12, day.Ilosc);
        Assert.Equal("1-10, 17-18", day.Pale);
        Assert.Equal(1, day.Strony);
    }

    [Fact]
    public void Logging_clears_the_input_box_ready_for_the_next_day()
    {
        LoadSchedule();

        _view.LogDay(D12, "1-12");

        Assert.Equal("", _view.JournalPiles);
    }

    [Fact]
    public void A_long_day_is_reported_as_more_than_one_page()
    {
        LoadSchedule();

        _view.LogDay(D12, "1-18");

        Assert.Equal(18, _view.Journal[0].Ilosc);
        Assert.Equal(2, _view.Journal[0].Strony);
    }

    [Fact]
    public void Two_days_stay_separate()
    {
        LoadSchedule();

        _view.LogDay(D12, "1-12");
        _view.LogDay(D13, "13-24");

        Assert.Equal(2, _view.Journal.Count);
        Assert.Equal(new[] { D12, D13 }, _view.Journal.Select(j => j.Data));
    }

    [Fact]
    public void Nonsense_in_the_pile_box_is_explained_not_thrown()
    {
        LoadSchedule();

        _view.LogDay(D12, "1-10, zonk");

        Assert.Empty(_view.Journal);
        Assert.Single(_view.Errors);
        Assert.Contains("zonk", _view.Errors[0]);
    }

    [Fact]
    public void Numbers_outside_the_schedule_are_queried_first()
    {
        LoadSchedule();

        _view.LogDay(D12, "1-5, 900");

        Assert.Contains(_view.Questions, q => q.Contains("900"));
        Assert.Equal(5, _view.Journal[0].Ilosc);    // the answer was yes; 900 skipped
    }

    [Fact]
    public void Declining_the_unknown_pile_question_logs_nothing()
    {
        LoadSchedule();
        _view.AnswerConfirm = (_, _) => false;

        _view.LogDay(D12, "1-5, 900");

        Assert.Empty(_view.Journal);
    }

    [Fact]
    public void Moving_a_pile_to_another_day_is_confirmed_first()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-5");

        _view.LogDay(D13, "3");

        Assert.Contains(_view.Questions, q => q.Contains("13.09.2022"));
        Assert.Equal(new[] { 4, 1 }, _view.Journal.Select(j => j.Ilosc));   // 1,2,4,5 then 3
    }

    [Fact]
    public void Declining_the_move_leaves_the_pile_where_it_was()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-5");
        _view.AnswerConfirm = (_, _) => false;

        _view.LogDay(D13, "3");

        var day = Assert.Single(_view.Journal);
        Assert.Equal(5, day.Ilosc);
    }

    [Fact]
    public void Logging_before_a_schedule_is_loaded_says_so()
    {
        _view.LogDay(D12, "1-12");

        Assert.Single(_view.Infos);
        Assert.Contains("wczytaj tabelkę", _view.Infos[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Logging_nothing_says_so()
    {
        LoadSchedule();

        _view.LogDay(D12, "");

        Assert.Contains(_view.Infos, i => i.Contains("Podaj numery"));
    }

    [Fact]
    public void Selected_piles_can_be_logged_instead_of_typed()
    {
        LoadSchedule();
        _view.SelectedPileNumbers = new[] { 4, 5, 6 };
        _view.JournalDate = D13;

        _view.ClickAddSelected();

        Assert.Equal("4-6", _view.Journal[0].Pale);
    }

    [Fact]
    public void Logging_selected_piles_with_nothing_selected_says_so()
    {
        LoadSchedule();

        _view.ClickAddSelected();

        Assert.Contains(_view.Infos, i => i.Contains("Zaznacz"));
    }

    [Fact]
    public void Removing_a_day_returns_its_piles_to_the_pool()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-12");
        _view.SelectedJournalDay = D12;

        _view.ClickRemoveDay();

        Assert.Empty(_view.Journal);
        Assert.Contains("Bez daty: 36", _view.StatusText);
    }

    [Fact]
    public void Removing_a_day_is_confirmed_first()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-12");
        _view.SelectedJournalDay = D12;
        _view.AnswerConfirm = (_, _) => false;

        _view.ClickRemoveDay();

        Assert.Single(_view.Journal);
    }

    [Fact]
    public void Removing_with_no_day_selected_says_so()
    {
        LoadSchedule();

        _view.ClickRemoveDay();

        Assert.Contains(_view.Infos, i => i.Contains("Zaznacz dzień"));
    }

    // ------------------------------------------------------------ recompute

    [Fact]
    public void Changing_the_coefficient_recalculates_every_pile()
    {
        LoadSchedule();

        _view.ChangeConcreteFactor(1.0);

        Assert.Equal(0.88, _view.Piles[0].Concrete);    // 7 m, no overbreak
    }

    [Fact]
    public void Changing_the_coefficient_updates_the_journal_totals()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-12");
        var before = _view.Journal[0].Beton;

        _view.ChangeConcreteFactor(1.0);

        Assert.True(_view.Journal[0].Beton < before);
    }

    [Fact]
    public void Changing_the_plant_applies_to_every_pile()
    {
        LoadSchedule();

        _view.ChangeConcretePlant("Lafarge");

        Assert.All(_view.Piles, p => Assert.Equal("Lafarge", p.ConcretePlant));
    }

    [Fact]
    public void Correcting_a_driven_length_recalculates_that_pile()
    {
        LoadSchedule();
        _view.Piles[0].ActualLength = 9;

        _view.EditPile(0, nameof(Pile.ActualLength));

        Assert.Equal(1.47, _view.Piles[0].Concrete);
    }

    [Fact]
    public void Editing_a_column_that_does_not_affect_volume_changes_nothing()
    {
        LoadSchedule();
        var before = _view.Piles[0].Concrete;

        _view.EditPile(0, nameof(Pile.Reinforcement));

        Assert.Equal(before, _view.Piles[0].Concrete);
    }

    [Fact]
    public void Changing_piles_per_page_repaginates_the_journal()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-12");

        _view.ChangePilesPerPage(6);

        Assert.Equal(2, _view.Journal[0].Strony);
    }

    // ------------------------------------------------------------- generate

    [Fact]
    public void Generating_writes_the_logged_days()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-12");
        _view.LogDay(D13, "13-24");
        _view.MetrykiPath = @"C:\wyjscie\metryki.xlsx";

        _view.ClickGenerate();

        Assert.Equal(1, _writer.Calls);
        Assert.Equal(@"C:\wyjscie\metryki.xlsx", _writer.Path);
        Assert.Equal(new[] { D12, D13 }, _writer.Days.Select(d => d.Date));
        Assert.Equal(new[] { 12, 12 }, _writer.Days.Select(d => d.Piles.Count));
    }

    [Fact]
    public void Generating_warns_about_piles_with_no_date()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-12");
        _view.MetrykiPath = @"C:\wyjscie\metryki.xlsx";

        _view.ClickGenerate();

        Assert.Contains(_view.Questions, q => q.Contains("24 pali nie ma"));
    }

    [Fact]
    public void Declining_that_warning_writes_nothing()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-12");
        _view.MetrykiPath = @"C:\wyjscie\metryki.xlsx";
        _view.AnswerConfirm = (title, _) => title != "Pale bez daty";

        _view.ClickGenerate();

        Assert.Equal(0, _writer.Calls);
    }

    [Fact]
    public void Generating_an_empty_journal_says_so_instead_of_writing()
    {
        LoadSchedule();

        _view.ClickGenerate();

        Assert.Equal(0, _writer.Calls);
        Assert.Contains(_view.Infos, i => i.Contains("Dziennik jest pusty"));
    }

    [Fact]
    public void Cancelling_the_save_dialog_writes_nothing()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-12");
        _view.MetrykiPath = null;

        _view.ClickGenerate();

        Assert.Equal(0, _writer.Calls);
    }

    [Fact]
    public void Generating_suggests_a_dated_file_name()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-12");
        _view.MetrykiPath = @"C:\wyjscie\metryki.xlsx";

        _view.ClickGenerate();

        Assert.StartsWith("Metryki pali ", _view.SuggestedMetrykiName);
        Assert.EndsWith(".xlsx", _view.SuggestedMetrykiName);
    }

    [Fact]
    public void A_failed_write_is_reported_and_the_window_is_usable_again()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-12");
        _view.MetrykiPath = @"C:\wyjscie\metryki.xlsx";
        _writer.FailWith = new IOException("Plik jest otwarty w programie Excel");

        _view.ClickGenerate();

        Assert.Single(_view.Errors);
        Assert.Contains("otwarty w programie Excel", _view.Errors[0]);
        Assert.False(_view.Busy);
    }

    [Fact]
    public void The_generated_file_can_be_opened_afterwards()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-12");
        _view.MetrykiPath = @"C:\wyjscie\metryki.xlsx";
        _view.AnswerConfirm = (title, _) => title != "Gotowe";   // do not open straight away

        _view.ClickGenerate();
        _view.ClickOpenOutput();

        Assert.True(_view.CanOpenOutput);
        Assert.Equal(@"C:\wyjscie\metryki.xlsx", Assert.Single(_view.Opened));
    }

    [Fact]
    public void Answering_yes_to_the_result_dialog_opens_the_file_at_once()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-12");
        _view.MetrykiPath = @"C:\wyjscie\metryki.xlsx";

        _view.ClickGenerate();

        Assert.Equal(@"C:\wyjscie\metryki.xlsx", Assert.Single(_view.Opened));
    }

    // ------------------------------------------------ generate selected days

    [Fact]
    public void Generating_selected_days_writes_only_those_days()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-12");
        _view.LogDay(D13, "13-24");
        _view.MetrykiPath = @"C:\wyjscie\dzien.xlsx";

        _view.ClickGenerateSelectedDays(D13);

        Assert.Equal(1, _writer.Calls);
        var day = Assert.Single(_writer.Days);
        Assert.Equal(D13, day.Date);
        Assert.Equal(Enumerable.Range(13, 12), day.Piles.Select(p => p.Number));
    }

    [Fact]
    public void Several_days_can_be_generated_together()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-12");
        _view.LogDay(D13, "13-24");
        _view.LogDay(D13.AddDays(1), "25-36");
        _view.MetrykiPath = @"C:\wyjscie\dni.xlsx";

        _view.ClickGenerateSelectedDays(D13.AddDays(1), D12);

        Assert.Equal(new[] { D12, D13.AddDays(1) }, _writer.Days.Select(d => d.Date));
    }

    [Fact]
    public void Generating_with_no_day_selected_says_so_instead_of_writing()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-12");

        _view.ClickGenerateSelectedDays();

        Assert.Equal(0, _writer.Calls);
        Assert.Contains(_view.Infos, i => i.Contains("Zaznacz dzień"));
    }

    [Fact]
    public void Generating_one_day_does_not_warn_about_piles_logged_on_no_day()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-12");
        _view.MetrykiPath = @"C:\wyjscie\dzien.xlsx";
        _view.AnswerConfirm = (title, _) => title != "Gotowe";

        _view.ClickGenerateSelectedDays(D12);

        Assert.DoesNotContain(_view.Questions, q => q.StartsWith("Pale bez daty"));
        Assert.Equal(1, _writer.Calls);
    }

    [Fact]
    public void Generating_one_day_suggests_a_file_named_after_that_day()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-12");
        _view.LogDay(D13, "13-24");
        _view.MetrykiPath = @"C:\wyjscie\dzien.xlsx";

        _view.ClickGenerateSelectedDays(D13);
        Assert.Equal("Metryki pali 2022-09-13.xlsx", _view.SuggestedMetrykiName);

        _view.ClickGenerateSelectedDays(D12, D13);
        Assert.Equal("Metryki pali 2022-09-12 do 2022-09-13.xlsx", _view.SuggestedMetrykiName);
    }

    [Fact]
    public void A_generated_day_can_be_opened_afterwards()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-12");
        _view.MetrykiPath = @"C:\wyjscie\dzien.xlsx";
        _view.AnswerConfirm = (title, _) => title != "Gotowe";

        _view.ClickGenerateSelectedDays(D12);
        _view.ClickOpenOutput();

        Assert.Equal(@"C:\wyjscie\dzien.xlsx", Assert.Single(_view.Opened));
    }

    // --------------------------------------------------------------- saving

    [Fact]
    public void Editing_the_site_details_reaches_the_generated_metryki()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-12");
        _view.ChangeSiteDetails("Budynek przy ul. Piotrkowskiej.");
        _view.MetrykiPath = @"C:\wyjscie\metryki.xlsx";

        _view.ClickGenerate();

        Assert.Equal("Budynek przy ul. Piotrkowskiej.", _writer.Settings!.Budowa);
    }

    [Fact]
    public void Editing_the_site_details_is_remembered_next_time()
    {
        LoadSchedule();
        _view.ChangeSiteDetails("Budynek przy ul. Piotrkowskiej.");

        var next = new FakeMainView();
        new MainPresenter(next, _reader, _writer, _repository).Start();

        Assert.Equal("Budynek przy ul. Piotrkowskiej.", next.Budowa);
    }

    [Fact]
    public void Every_change_is_saved()
    {
        LoadSchedule();
        var afterLoad = _repository.Saves;

        _view.LogDay(D12, "1-12");

        Assert.True(_repository.Saves > afterLoad);
    }

    [Fact]
    public void Closing_the_window_saves()
    {
        LoadSchedule();
        var before = _repository.Saves;

        _view.CloseWindow();

        Assert.True(_repository.Saves > before);
    }

    [Fact]
    public void A_storage_failure_is_reported_without_losing_the_journal()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-12");
        _repository.FailNextSaveWith = new IOException("Dysk pełny");

        _view.CloseWindow();

        Assert.Contains("Nie udało się zapisać projektu", _view.StatusText);
        Assert.Single(_view.Journal);          // still on screen
    }

    [Fact]
    public void Saving_the_project_elsewhere_moves_where_it_is_kept()
    {
        LoadSchedule();
        _view.SaveProjectPath = @"D:\budowy\tuwima.mpali";

        _view.ClickSaveProjectAs();

        Assert.Equal(@"D:\budowy\tuwima.mpali", _presenter.ProjectPath);
        Assert.True(_repository.Has(@"D:\budowy\tuwima.mpali"));
    }

    [Fact]
    public void Opening_another_project_replaces_what_is_on_screen()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-12");
        _view.SaveProjectPath = @"D:\budowy\pierwsza.mpali";
        _view.ClickSaveProjectAs();

        _view.ClickNewProject();
        Assert.Empty(_view.Journal);

        _view.ProjectPath = @"D:\budowy\pierwsza.mpali";
        _view.ClickOpenProject();

        Assert.Single(_view.Journal);
        Assert.Equal(12, _view.Journal[0].Ilosc);
    }

    [Fact]
    public void Opening_a_project_that_cannot_be_read_is_reported()
    {
        _view.ProjectPath = @"D:\budowy\brak.mpali";

        _view.ClickOpenProject();

        Assert.Single(_view.Errors);
    }

    /// <summary>
    /// Starting a new project must not touch the file the user saved this site
    /// to - losing a site's journal that way would be silent and unrecoverable.
    /// </summary>
    [Fact]
    public void Starting_a_new_project_leaves_the_saved_project_file_alone()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-12");
        _view.SaveProjectPath = @"D:\budowy\tuwima.mpali";
        _view.ClickSaveProjectAs();

        _view.ClickNewProject();

        Assert.Equal(_repository.DefaultPath, _presenter.ProjectPath);
        Assert.Equal(12, _repository.Load(@"D:\budowy\tuwima.mpali")!.Piles.Count(p => p.Executed is not null));
    }

    [Fact]
    public void Starting_a_new_project_is_confirmed_first()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-12");
        _view.AnswerConfirm = (_, _) => false;

        _view.ClickNewProject();

        Assert.Single(_view.Journal);
    }

    // --------------------------------------------------------------- status

    [Fact]
    public void The_status_line_counts_piles_days_and_pages()
    {
        LoadSchedule();
        _view.LogDay(D12, "1-12");
        _view.LogDay(D13, "13-30");

        Assert.Contains("Pale: 36", _view.StatusText);
        Assert.Contains("W dzienniku: 30", _view.StatusText);
        Assert.Contains("Bez daty: 6", _view.StatusText);
        Assert.Contains("Dni: 2", _view.StatusText);
        Assert.Contains("Strony: 3", _view.StatusText);
    }

    [Fact]
    public void Generating_is_only_offered_once_something_is_logged()
    {
        LoadSchedule();
        Assert.False(_view.CanGenerate);

        _view.LogDay(D12, "1-12");
        Assert.True(_view.CanGenerate);

        _view.SelectedJournalDay = D12;
        _view.ClickRemoveDay();
        Assert.False(_view.CanGenerate);
    }
}
