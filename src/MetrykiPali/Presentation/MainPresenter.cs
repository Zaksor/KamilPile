using MetrykiPali.Model;
using MetrykiPali.Services;

namespace MetrykiPali.Presentation;

/// <summary>
/// All of the application's behaviour.
///
/// The presenter reacts to what the user asked the view for, works on the
/// domain (<see cref="Journal"/>, <see cref="PileSchedule"/>) and pushes the
/// result back into the view. It talks to files only through the service
/// interfaces, so the tests drive the whole app with a fake view and a
/// fake repository - no window, no disk.
/// </summary>
public sealed class MainPresenter
{
    private readonly IMainView _view;
    private readonly IScheduleReader _reader;
    private readonly IMetrykaWriter _writer;
    private readonly IProjectRepository _repository;

    private ProjectState _project = new();
    private Journal _journal = new(new List<Pile>());
    private string _projectPath;
    private string _siteName = "";
    private string? _lastOutput;
    private bool _loading;

    public MainPresenter(
        IMainView view,
        IScheduleReader reader,
        IMetrykaWriter writer,
        IProjectRepository repository)
    {
        _view = view;
        _reader = reader;
        _writer = writer;
        _repository = repository;
        _projectPath = repository.DefaultPath;

        _view.LoadScheduleRequested += (_, _) => LoadSchedule();
        _view.AddDayRequested += (_, _) => AddTypedPilesToJournal();
        _view.AddSelectedPilesRequested += (_, _) => AddSelectedPilesToJournal();
        _view.RemoveDayRequested += (_, _) => RemoveSelectedDay();
        _view.GenerateRequested += (_, _) => Generate();
        _view.GenerateSelectedDaysRequested += (_, _) => GenerateSelectedDays();
        _view.OpenOutputRequested += (_, _) => OpenLastOutput();
        _view.SettingsChanged += (_, _) => SaveQuietly();
        _view.ConcreteFactorChanged += (_, _) => ConcreteFactorChanged();
        _view.PilesPerPageChanged += (_, _) => RefreshJournal();
        _view.ConcretePlantChanged += (_, _) => ConcretePlantChanged();
        _view.PileEdited += (_, edit) => PileEdited(edit);
        _view.DayFactorEdited += (_, edit) => DayFactorEdited(edit);
        _view.DayConcreteEdited += (_, edit) => DayConcreteEdited(edit);
        _view.ConcreteModeChanged += (_, _) => ConcreteModeChanged();
        _view.ThemeChanged += (_, _) => RememberTheme();
        _view.JournalOrderChanged += (_, _) => { RefreshJournal(); SaveQuietly(); };
        _view.MissingMetrykiRequested += (_, _) => ShowMissingMetryki();
        _view.FooterImageRequested += (_, _) => ChooseFooterImage();
        _view.FooterImageCleared += (_, _) => ClearFooterImage();
        _view.SiteSelected += (_, name) => SwitchSite(name);
        _view.NewSiteRequested += (_, _) => NewSite();
        _view.RenameSiteRequested += (_, _) => RenameSite();
        _view.OpenProjectRequested += (_, _) => ImportSite();
        _view.SaveProjectAsRequested += (_, _) => ExportSite();
        _view.ShowDataFolderRequested += (_, _) => _view.OpenExternally(_repository.DataDirectory);
        _view.ViewClosing += (_, _) => SaveQuietly();
    }

    /// <summary>Where the project is currently being saved.</summary>
    public string ProjectPath => _projectPath;

    /// <summary>The site (budowa) open now.</summary>
    public string SiteName => _siteName;

    /// <summary>The file written by the last successful generation, if any.</summary>
    public string? LastOutput => _lastOutput;

    // ------------------------------------------------------------- start-up

    /// <summary>
    /// Reopens the site that was open last. The first time, the single project
    /// kept before there were sites becomes the first site - copied, so the old
    /// file stays as it was.
    /// </summary>
    public void Start()
    {
        _view.Theme = _repository.Theme == ThemeDark ? AppTheme.Dark : AppTheme.Light;

        var sites = _repository.ListSites();
        if (sites.Count == 0)
        {
            var legacy = _repository.Load(_repository.DefaultPath);
            var name = SiteNames.Clean(legacy?.Settings.Budowa) ?? SiteNames.Fallback;
            _repository.Save(_repository.SitePath(name), legacy ?? new ProjectState());
            sites = new[] { name };
        }

        var last = _repository.LastSite;
        var open = sites.FirstOrDefault(s => string.Equals(s, last, StringComparison.OrdinalIgnoreCase)) ?? sites[0];
        OpenSite(open);
    }

    private void OpenSite(string name)
    {
        var path = _repository.SitePath(name);
        _repository.BackupOnce(path);
        var restored = _repository.Load(path);

        _siteName = name;
        _lastOutput = null;
        _view.CanOpenOutput = false;
        Adopt(restored ?? new ProjectState(), path);

        TryRemember(name);
        _view.ShowSites(_repository.ListSites(), name);
    }

    private const string ThemeLight = "Jasny", ThemeDark = "Ciemny";

    private void RememberTheme()
    {
        try { _repository.Theme = _view.Theme == AppTheme.Dark ? ThemeDark : ThemeLight; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* only a convenience */ }
    }

    private void TryRemember(string name)
    {
        try { _repository.LastSite = name; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* only a convenience */ }
    }

    private void Adopt(ProjectState project, string path)
    {
        _project = project;
        _projectPath = path;
        _journal = new Journal(_project.Piles);

        // A file from before days had their own coefficient: pin its logged days
        // to the coefficient their volumes were computed with.
        _journal.PinMissingFactors(_project.Settings.ConcreteFactor);
        _project.Version = new ProjectState().Version;

        _loading = true;
        try
        {
            var s = _project.Settings;
            _view.SourcePath = _project.SourcePath ?? "";
            _view.Budowa = s.Budowa;
            _view.Wykonawca = s.Wykonawca;
            _view.Metoda = s.Metoda;
            _view.Betoniarnia = s.Betoniarnia;
            _view.ConcreteFactor = s.ConcreteFactor;
            _view.PilesPerPage = s.PilesPerPage;
            _view.OutputFormat = s.Format;
            _view.FooterText = s.Firma;
            _view.FooterImagePosition = s.FooterImagePosition;
            _view.FooterImageLabel = FooterImageLabel(s);
            _view.ConcreteMode = s.ConcreteMode;
            _view.JournalNewestFirst = s.JournalNewestFirst;
            _view.JournalConcreteUsed = null;
            _view.JournalDate = s.Data == default ? DateTime.Today : s.Data;

            _view.ShowRanges(_project.Ranges);
            _view.ShowPiles(_project.Piles);
        }
        finally
        {
            _loading = false;
        }

        RefreshJournal();
    }

    // -------------------------------------------------------------- loading

    private void LoadSchedule()
    {
        var path = _view.AskForSchedule();
        if (path is null) return;

        try
        {
            CaptureSettings();

            var ranges = _reader.Read(path).ToList();

            var duplicates = PileSchedule.DuplicateNumbers(ranges);
            if (duplicates.Count > 0)
            {
                _view.ShowError("Powtarzające się numery pali",
                    $"Te numery pali występują w tabelce więcej niż raz:\n{PileNumbers.Format(duplicates)}\n\n" +
                    "Każdy pal musi mieć własny numer. Popraw tabelkę i wczytaj ją ponownie.");
                return;
            }

            var fresh = PileSchedule.Expand(ranges, _project.Settings);
            var kept = PileSchedule.CarryOverDates(_project.Piles, fresh);
            new Journal(fresh).RecalculateDays(_view.ConcreteFactor);

            _project.SourcePath = path;
            _project.Ranges = ranges;
            _project.Piles = fresh;

            Adopt(_project, _projectPath);
            Save();

            _view.StatusText = kept > 0
                ? $"Wczytano {ranges.Count} zakresów. Zachowano daty dla {kept} pali."
                : $"Wczytano {ranges.Count} zakresów ({fresh.Count} pali).";

            var missing = PileSchedule.MissingNumbers(ranges);
            if (missing.Count > 0)
                _view.ShowInfo("Brakujące numery pali",
                    $"Wczytano {fresh.Count} pali, ale w tabelce nie ma numerów:\n{PileNumbers.Format(missing)}\n\n" +
                    "Jeśli projektant celowo ich pominął, nic nie trzeba robić.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException)
        {
            _view.ShowError("Nie udało się wczytać pliku", ex.Message);
        }
    }

    // -------------------------------------------------------------- journal

    private void AddTypedPilesToJournal()
    {
        List<int> numbers;
        try
        {
            numbers = PileNumbers.Parse(_view.JournalPiles);
        }
        catch (FormatException ex)
        {
            _view.ShowError("Błędny zapis numerów pali", ex.Message);
            return;
        }

        if (AddToJournal(numbers)) _view.JournalPiles = "";
    }

    private void AddSelectedPilesToJournal()
    {
        var selected = _view.SelectedPileNumbers;
        if (selected.Count == 0)
        {
            _view.ShowInfo("Brak zaznaczenia", "Zaznacz najpierw pale na zakładce \"Pale\".");
            return;
        }

        AddToJournal(selected);
    }

    private bool AddToJournal(IReadOnlyCollection<int> numbers)
    {
        if (_journal.Total == 0)
        {
            _view.ShowInfo("Brak danych", "Najpierw wczytaj tabelkę z palami.");
            return false;
        }

        if (numbers.Count == 0)
        {
            _view.ShowInfo("Brak numerów", "Podaj numery pali, np. \"1-10, 25, 30-33\".");
            return false;
        }

        var plan = _journal.Plan(numbers, _view.JournalDate);

        if (plan.Unknown.Count > 0 && !_view.Confirm("Nieznane numery pali",
                $"Tych pali nie ma w tabelce i zostaną pominięte:\n{PileNumbers.Format(plan.Unknown)}\n\nKontynuować?"))
            return false;

        if (plan.Moved.Count > 0 && !_view.Confirm("Pale już w dzienniku",
                $"Te pale mają już inną datę wykonania:\n{PileNumbers.Format(plan.Moved)}\n\n" +
                $"Przenieść je na {plan.Date:dd.MM.yyyy}?"))
            return false;

        if (!plan.HasAnythingToDo)
        {
            _view.ShowInfo("Brak danych", "Żaden z podanych numerów nie występuje w tabelce.");
            return false;
        }

        var added = _journal.Apply(plan, _view.ConcreteFactor);

        // On the concrete-used method, the figure typed with the piles is the
        // day's total - what was delivered that day, not an addition to it.
        if (_view.ConcreteMode == ConcreteMode.Measured && _view.JournalConcreteUsed is { } used && used > 0
            && ConfirmConcreteUsed(plan.Date, used))
        {
            _journal.SetDayConcreteUsed(plan.Date, Math.Round(used, 2), _view.ConcreteFactor);
            _view.JournalConcreteUsed = null;
        }

        RefreshJournal();
        Save();

        var how = _journal.ConcreteUsedOf(plan.Date) is { } total
            ? $"beton zużyty {total:0.00} m³"
            : _view.ConcreteMode == ConcreteMode.Measured
                ? "wpisz ilość zużytego betonu w kolumnie \"Zużyto\" w dzienniku"
                : $"wsp. betonu {_journal.FactorOf(plan.Date):0.00}";

        _view.StatusText = $"Dodano {added} pali do dnia {plan.Date:dd.MM.yyyy} ({how})." +
                           (plan.Moved.Count > 0 ? $" Przeniesiono {plan.Moved.Count}." : "");
        return true;
    }

    private void RemoveSelectedDay()
    {
        var day = _view.SelectedJournalDay;
        if (day is null)
        {
            _view.ShowInfo("Brak zaznaczenia", "Zaznacz dzień na liście \"Dziennik (dni)\".");
            return;
        }

        var count = _journal.Days().FirstOrDefault(d => d.Date == day.Value.Date)?.Piles.Count ?? 0;
        if (!_view.Confirm("Usuń dzień",
                $"Usunąć dzień {day:dd.MM.yyyy} ({count} pali)?\n\nPale wrócą na listę nieprzypisanych."))
            return;

        _journal.RemoveDay(day.Value, _view.ConcreteFactor);
        RefreshJournal();
        Save();
    }

    private void RefreshJournal()
    {
        _view.ShowJournal(_journal.Entries(_view.PilesPerPage, _view.JournalNewestFirst));
        _view.RefreshPiles();
        _view.CanGenerate = _journal.Assigned > 0;

        var missing = _journal.Missing();
        var count = missing.Undated.Count + missing.Days.Sum(d => d.Numbers.Count);
        _view.MissingMetrykiText = _journal.Total == 0 ? ""
            : count == 0 ? "Wszystkie pale mają metryki ✓"
            : $"Pale bez metryk: {count} — pokaż które";

        UpdateStatus();
    }

    // -------------------------------------------------------- missing report

    private void ShowMissingMetryki()
    {
        if (_journal.Total == 0)
        {
            _view.ShowInfo("Brak danych", "Najpierw wczytaj tabelkę z palami.");
            return;
        }

        _view.ShowReport("Które pale nie mają metryk", MissingReport(_journal.Missing()));
    }

    private string MissingReport(MissingMetryki missing)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine($"Budowa: {_siteName}");
        text.AppendLine($"Stan na {DateTime.Now:dd.MM.yyyy HH:mm}, pali w tabelce: {_journal.Total}");
        text.AppendLine();

        if (missing.None)
        {
            text.AppendLine("Wszystkie pale mają daty wykonania i wygenerowane metryki.");
            return text.ToString();
        }

        if (missing.Undated.Count > 0)
        {
            text.AppendLine($"PALE BEZ DATY WYKONANIA (nie ma ich jeszcze w dzienniku): {missing.Undated.Count}");
            text.AppendLine(PileNumbers.Format(missing.Undated));
            text.AppendLine();
        }

        if (missing.Days.Count > 0)
        {
            text.AppendLine($"DNI W DZIENNIKU BEZ WYGENEROWANYCH METRYK: {missing.Days.Count}");
            text.AppendLine("(metryk jeszcze nie generowano albo dzień zmieniono po ich wygenerowaniu)");
            foreach (var day in missing.Days)
                text.AppendLine($"  {day.Date:dd.MM.yyyy}  —  {day.Numbers.Count} z {day.DayCount} pali:  {PileNumbers.Format(day.Numbers)}");
            text.AppendLine();
        }
        else
        {
            text.AppendLine("Wszystkie dni z dziennika mają wygenerowane metryki.");
        }

        return text.ToString();
    }

    // ---------------------------------------------------------------- footer

    private static string FooterImageLabel(MetrykaSettings s)
        => s.FooterImage is { Length: > 0 } ? s.FooterImageName ?? "obraz" : "brak";

    private void ChooseFooterImage()
    {
        var path = _view.AskForImage();
        if (path is null) return;

        try
        {
            _project.Settings.FooterImage = FooterImage.Prepare(path);
            _project.Settings.FooterImageName = Path.GetFileName(path);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _view.ShowError("Nie udało się wczytać obrazu", ex.Message);
            return;
        }

        _view.FooterImageLabel = FooterImageLabel(_project.Settings);
        SaveQuietly();
        _view.StatusText = $"Obraz w stopce: {_project.Settings.FooterImageName}. Pojawi się na każdej stronie metryk.";
    }

    private void ClearFooterImage()
    {
        _project.Settings.FooterImage = null;
        _project.Settings.FooterImageName = null;
        _view.FooterImageLabel = FooterImageLabel(_project.Settings);
        SaveQuietly();
    }

    // ------------------------------------------------------------ recompute

    private void ConcreteFactorChanged()
    {
        if (_loading) return;

        _journal.RecalculateConcrete(_view.ConcreteFactor);
        RefreshJournal();
        SaveQuietly();

        if (_journal.Assigned > 0)
            _view.StatusText = $"Wsp. betonu {_view.ConcreteFactor:0.00} dotyczy dni wpisanych od teraz. " +
                               "Dni już w dzienniku zachowują swój — zmienisz go w kolumnie \"Wsp. betonu\" w dzienniku.";
    }

    /// <summary>Minimum and maximum the coefficient field allows; a day is held to the same.</summary>
    public const double MinFactor = 1.00, MaxFactor = 3.00;

    private void DayFactorEdited(DayFactorEdited edit)
    {
        if (_loading) return;

        var factor = Math.Round(edit.Factor, 2);
        if (factor < MinFactor || factor > MaxFactor)
        {
            _view.ShowError("Błędny współczynnik",
                $"Współczynnik betonu musi być między {MinFactor:0.00} a {MaxFactor:0.00}.");
            RefreshJournal();
            return;
        }

        var changed = _journal.SetDayFactor(edit.Date, factor);
        RefreshJournal();
        SaveQuietly();

        if (changed > 0)
            _view.StatusText = $"Dzień {edit.Date:dd.MM.yyyy}: wsp. betonu {factor:0.00}, przeliczono {changed} pali.";
    }

    private void DayConcreteEdited(DayConcreteEdited edit)
    {
        if (_loading) return;

        if (edit.Total is not > 0)
        {
            _journal.SetDayConcreteUsed(edit.Date, null, _view.ConcreteFactor);
            RefreshJournal();
            SaveQuietly();
            _view.StatusText = $"Dzień {edit.Date:dd.MM.yyyy}: beton liczony ze współczynnika {_journal.FactorOf(edit.Date):0.00}.";
            return;
        }

        var total = Math.Round(edit.Total.Value, 2);
        if (total > MaxDayConcrete)
        {
            _view.ShowError("Błędna ilość betonu", $"Ilość betonu z jednego dnia nie może przekraczać {MaxDayConcrete} m³.");
            RefreshJournal();
            return;
        }

        if (!ConfirmConcreteUsed(edit.Date, total))
        {
            RefreshJournal();
            return;
        }

        var piles = _journal.SetDayConcreteUsed(edit.Date, total, _view.ConcreteFactor);
        RefreshJournal();
        SaveQuietly();
        _view.StatusText = $"Dzień {edit.Date:dd.MM.yyyy}: {total:0.00} m³ betonu rozdzielono na {piles} pali według ich objętości.";
    }

    /// <summary>More than any day on a pile site could use; stops a slip of the keyboard.</summary>
    public const double MaxDayConcrete = 10000;

    /// <summary>
    /// A typo in the concrete used would put wrong figures on every pile of the
    /// day, so a total below the piles' theoretical volume, or above 2.5 times
    /// it, is put to the user first.
    /// </summary>
    private bool ConfirmConcreteUsed(DateTime date, double total)
    {
        var theoretical = _journal.TheoreticalVolumeOf(date);
        if (theoretical <= 0) return true;

        var ratio = total / theoretical;
        if (ratio is >= 1.0 and <= 2.5) return true;

        return _view.Confirm("Sprawdź ilość betonu",
            $"{total:0.00} m³ na dzień {date:dd.MM.yyyy} to {ratio:0.00} × objętość teoretyczna tych pali ({theoretical:0.00} m³).\n" +
            (ratio < 1.0 ? "To mniej niż sama objętość otworów." : "To ponad dwa i pół raza więcej niż objętość otworów.") +
            "\n\nZapisać mimo to?");
    }

    private void ConcreteModeChanged()
    {
        if (_loading) return;

        SaveQuietly();
        _view.StatusText = _view.ConcreteMode == ConcreteMode.Measured
            ? "Beton z ilości zużytej: przy wpisywaniu dnia podaj ilość betonu w karcie BETON (albo wpisz ją później w kolumnie \"Zużyto\"). " +
              "Program rozdzieli go na pale według ich objętości."
            : "Beton ze współczynnika: objętość teoretyczna × współczynnik dnia.";
    }

    private void ConcretePlantChanged()
    {
        if (_loading) return;

        _journal.SetConcretePlant(_view.Betoniarnia);
        _view.RefreshPiles();
        SaveQuietly();
    }

    private void PileEdited(PileEdited edit)
    {
        if (_loading) return;
        if (edit.RowIndex < 0 || edit.RowIndex >= _project.Piles.Count) return;
        if (edit.PropertyName is not (nameof(Pile.ActualLength) or nameof(Pile.Diameter))) return;

        var pile = _project.Piles[edit.RowIndex];
        Journal.Invalidate(pile);
        _journal.RecalculateConcrete(pile, _view.ConcreteFactor);
        RefreshJournal();
        SaveQuietly();
    }

    // ------------------------------------------------------------ generate

    private void Generate()
    {
        CaptureSettings();

        var days = _journal.Days();
        if (days.Count == 0)
        {
            _view.ShowInfo("Brak danych",
                "Dziennik jest pusty — najpierw dodaj pale wykonane w poszczególnych dniach.");
            return;
        }

        if (_journal.Outstanding > 0 && !_view.Confirm("Pale bez daty",
                $"{_journal.Outstanding} pali nie ma jeszcze przypisanej daty i nie znajdzie się w metrykach.\n\n" +
                "Wygenerować metryki tylko dla pali z dziennika?"))
            return;

        WriteMetryki(days, $"Metryki pali {DateTime.Today:yyyy-MM-dd}");
    }

    /// <summary>
    /// Generates only the days picked in the journal - typically the one just
    /// finished, so its metryki can be handed over without waiting for the end
    /// of the job.
    /// </summary>
    private void GenerateSelectedDays()
    {
        CaptureSettings();

        var selected = _view.SelectedJournalDays.Select(d => d.Date).ToHashSet();
        var days = _journal.Days().Where(d => selected.Contains(d.Date)).ToList();
        if (days.Count == 0)
        {
            _view.ShowInfo("Brak zaznaczenia",
                "Zaznacz dzień (albo kilka dni) na liście \"Dziennik (dni)\", dla których chcesz wygenerować metryki.");
            return;
        }

        var first = days[0].Date;
        var last = days[^1].Date;
        var name = first == last
            ? $"Metryki pali {first:yyyy-MM-dd}"
            : $"Metryki pali {first:yyyy-MM-dd} do {last:yyyy-MM-dd}";

        WriteMetryki(days, name);
    }

    /// <summary>
    /// Asks where to save and writes the days there, in the format picked next
    /// to the generate buttons - the extension of the suggested name carries it.
    /// </summary>
    private void WriteMetryki(List<WorkDay> days, string suggestedName)
    {
        var extension = _project.Settings.Format == MetrykaFormat.Pdf ? ".pdf" : ".xlsx";
        var path = _view.AskWhereToSaveMetryki(suggestedName + extension);
        if (path is null) return;

        try
        {
            _view.Busy = true;
            _writer.Write(path, days, _project.Settings);

            _lastOutput = path;
            _view.CanOpenOutput = true;

            _journal.MarkGenerated(days, DateTime.Now);
            RefreshJournal();
            SaveQuietly();

            var pages = MetrykaWriter.Paginate(days, _project.Settings.PilesPerPage).Count;
            var piles = days.Sum(d => d.Piles.Count);

            if (_view.Confirm("Gotowe",
                    $"Zapisano {pages} stron metryk ({piles} pali, {days.Count} dni) do:\n{path}\n\nOtworzyć plik teraz?"))
                OpenLastOutput();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _view.ShowError("Nie udało się zapisać pliku", ex.Message);
        }
        finally
        {
            _view.Busy = false;
            UpdateStatus();
        }
    }

    private void OpenLastOutput()
    {
        if (_lastOutput is not null) _view.OpenExternally(_lastOutput);
    }

    // ---------------------------------------------------------------- sites

    private void SwitchSite(string name)
    {
        if (string.Equals(name, _siteName, StringComparison.OrdinalIgnoreCase)) return;
        if (!_repository.ListSites().Contains(name, StringComparer.OrdinalIgnoreCase)) return;

        SaveQuietly();
        OpenSite(name);
        _view.StatusText = $"Otwarto budowę \"{name}\".";
    }

    /// <summary>
    /// Adds a site and switches to it. The new site starts with no schedule and
    /// no journal, but keeps the contractor, method, plant and coefficient of
    /// the current one - they rarely change from site to site.
    /// </summary>
    private void NewSite()
    {
        var name = AskForSiteName("Nowa budowa", "Nazwa nowej budowy (np. adres albo nazwa inwestycji):", "");
        if (name is null) return;

        SaveQuietly();
        CaptureSettings();

        var current = _project.Settings;
        var fresh = new ProjectState
        {
            Settings = new MetrykaSettings
            {
                Budowa = name,
                Wykonawca = current.Wykonawca,
                Metoda = current.Metoda,
                Betoniarnia = current.Betoniarnia,
                Firma = current.Firma,
                DokumentacjaNaglowek = current.DokumentacjaNaglowek,
                FooterImage = current.FooterImage,
                FooterImageName = current.FooterImageName,
                FooterImagePosition = current.FooterImagePosition,
                ConcreteMode = current.ConcreteMode,
                JournalNewestFirst = current.JournalNewestFirst,
                ConcreteFactor = current.ConcreteFactor,
                PilesPerPage = current.PilesPerPage,
                Format = current.Format
            }
        };

        try
        {
            _repository.Save(_repository.SitePath(name), fresh);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _view.ShowError("Nie udało się utworzyć budowy", ex.Message);
            return;
        }

        OpenSite(name);
        _view.StatusText = $"Utworzono budowę \"{name}\". Wczytaj jej tabelkę z palami.";
    }

    private void RenameSite()
    {
        var name = AskForSiteName("Zmień nazwę budowy", "Nowa nazwa budowy:", _siteName, allowCurrent: true);
        if (name is null || name == _siteName) return;

        SaveQuietly();
        try
        {
            _repository.RenameSite(_siteName, name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _view.ShowError("Nie udało się zmienić nazwy", ex.Message);
            return;
        }

        _siteName = name;
        _projectPath = _repository.SitePath(name);
        TryRemember(name);
        _view.ShowSites(_repository.ListSites(), name);
        _view.StatusText = $"Zmieniono nazwę budowy na \"{name}\".";
    }

    /// <summary>Asks for a site name until it is usable and free, or the user gives up.</summary>
    private string? AskForSiteName(string title, string prompt, string initial, bool allowCurrent = false)
    {
        var text = initial;
        while (true)
        {
            text = _view.AskForText(title, prompt, text);
            if (text is null) return null;

            var name = SiteNames.Clean(text);
            if (name is null)
            {
                _view.ShowError(title, "Podaj nazwę budowy.");
                continue;
            }

            var taken = _repository.ListSites().Where(s => !(allowCurrent && string.Equals(s, _siteName, StringComparison.OrdinalIgnoreCase)));
            if (taken.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                _view.ShowError(title, $"Budowa o nazwie \"{name}\" już istnieje. Wybierz ją z listy albo podaj inną nazwę.");
                continue;
            }

            return name;
        }
    }

    /// <summary>
    /// Brings a site saved as a file (from another computer, or a copy made with
    /// "Zapisz kopię budowy") into the list and opens it.
    /// </summary>
    private void ImportSite()
    {
        var path = _view.AskForProject();
        if (path is null) return;

        var loaded = _repository.Load(path);
        if (loaded is null)
        {
            _view.ShowError("Błąd", "Nie udało się odczytać tego pliku budowy.");
            return;
        }

        var name = SiteNames.Unique(
            SiteNames.Clean(Path.GetFileNameWithoutExtension(path)) ?? SiteNames.Fallback,
            _repository.ListSites());

        SaveQuietly();
        _repository.Save(_repository.SitePath(name), loaded);
        OpenSite(name);
        _view.StatusText = $"Dodano budowę \"{name}\" z pliku {Path.GetFileName(path)}.";
    }

    /// <summary>Writes a copy of the open site to a file of the user's choice; the site stays where it is.</summary>
    private void ExportSite()
    {
        var path = _view.AskWhereToSaveProject(_siteName + JsonProjectRepository.FileExtension);
        if (path is null) return;

        try
        {
            Save();
            _repository.Save(path, _project);
            _view.StatusText = "Zapisano kopię budowy: " + path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _view.ShowError("Nie udało się zapisać kopii", ex.Message);
        }
    }

    // --------------------------------------------------------------- saving

    private void CaptureSettings()
    {
        var s = _project.Settings;
        s.Budowa = _view.Budowa.Trim();
        s.Wykonawca = _view.Wykonawca.Trim();
        s.Metoda = _view.Metoda.Trim();
        s.Betoniarnia = _view.Betoniarnia.Trim();
        s.ConcreteFactor = _view.ConcreteFactor;
        s.PilesPerPage = _view.PilesPerPage;
        s.Format = _view.OutputFormat;
        s.Firma = _view.FooterText.Trim();
        s.FooterImagePosition = _view.FooterImagePosition;
        s.ConcreteMode = _view.ConcreteMode;
        s.JournalNewestFirst = _view.JournalNewestFirst;
        s.Data = _view.JournalDate.Date;
    }

    private void Save()
    {
        CaptureSettings();
        _repository.Save(_projectPath, _project);
    }

    /// <summary>Saves without letting a storage problem interrupt the user.</summary>
    private void SaveQuietly()
    {
        if (_loading) return;

        try
        {
            Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _view.StatusText = "Nie udało się zapisać projektu: " + ex.Message;
        }
    }

    // --------------------------------------------------------------- status

    /// <summary>
    /// The counts and progress of the open site. The theoretical volumes are
    /// pure geometry - diameter and design length, no coefficient - so the
    /// concrete bar measures progress against what the schedule itself implies.
    /// </summary>
    private SiteProgress Progress()
    {
        var piles = _project.Piles;
        var logged = piles.Where(p => p.Executed is not null).ToList();
        var missing = _journal.Missing();

        return new SiteProgress(
            Ranges: _project.Ranges.Count,
            Piles: piles.Count,
            Logged: logged.Count,
            Days: _journal.Days().Count,
            Pages: _journal.Entries(_view.PilesPerPage).Sum(e => e.Strony),
            VolumeAll: Math.Round(piles.Sum(p => PileMath.TheoreticalVolume(p.Diameter, p.DesignLength)), 2),
            VolumeLogged: Math.Round(logged.Sum(p => PileMath.TheoreticalVolume(p.Diameter, p.DesignLength)), 2),
            ConcreteLogged: Math.Round(logged.Sum(p => p.Concrete), 2),
            WithoutMetryka: missing.Undated.Count + missing.Days.Sum(d => d.Numbers.Count));
    }

    /// <summary>The counts go to the progress display; the status line is left for messages.</summary>
    private void UpdateStatus()
    {
        _view.ShowProgress(Progress());
        _view.StatusText = _journal.Total == 0
            ? "Wczytaj tabelkę z palami, aby rozpocząć."
            : _lastOutput is null ? "" : $"Ostatnio zapisano: {Path.GetFileName(_lastOutput)}";
    }
}
