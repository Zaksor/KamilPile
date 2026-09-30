namespace MetrykiPali.Tests;

/// <summary>
/// A window that never opens. It records what the presenter put on screen and
/// what it asked the user, and lets a test answer those questions and press the
/// buttons - which is the whole point of keeping the real view passive.
/// </summary>
internal sealed class FakeMainView : IMainView
{
    // --- what the window is showing ------------------------------------
    public string SourcePath { get; set; } = "";
    public string Budowa { get; set; } = "Budowa testowa.";
    public string Wykonawca { get; set; } = "Wykonawca sp. z o.o.";
    public string Metoda { get; set; } = "CFA";
    public string Betoniarnia { get; set; } = "Bosta";
    public double ConcreteFactor { get; set; } = 1.30;
    public int PilesPerPage { get; set; } = 12;
    public MetrykaFormat OutputFormat { get; set; } = MetrykaFormat.Xlsx;
    public string FooterText { get; set; } = "GREIFBAU SP. Z O.O.";
    public FooterPosition FooterImagePosition { get; set; } = FooterPosition.Center;
    public string FooterImageLabel { get; set; } = "";
    public string MissingMetrykiText { get; set; } = "";
    public ConcreteMode ConcreteMode { get; set; } = ConcreteMode.Factor;
    public double? JournalConcreteUsed { get; set; }
    public AppTheme Theme { get; set; } = AppTheme.Light;
    public bool JournalNewestFirst { get; set; }
    public SiteProgress? Progress { get; private set; }
    public void ShowProgress(SiteProgress progress) => Progress = progress;

    /// <summary>The file the image dialog returns; null means the user cancelled.</summary>
    public string? ImagePath { get; set; }
    public string? AskForImage() => ImagePath;

    public List<(string Title, string Text)> Reports { get; } = new();
    public void ShowReport(string title, string text) => Reports.Add((title, text));
    public DateTime JournalDate { get; set; } = new(2022, 9, 12);
    public string JournalPiles { get; set; } = "";
    public string StatusText { get; set; } = "";
    public bool CanGenerate { get; set; }
    public bool CanOpenOutput { get; set; }
    public bool Busy { get; set; }

    // --- what the user has picked --------------------------------------
    public DateTime? SelectedJournalDay { get; set; }
    public IReadOnlyList<DateTime> SelectedJournalDays { get; set; } = Array.Empty<DateTime>();
    public IReadOnlyList<int> SelectedPileNumbers { get; set; } = Array.Empty<int>();

    // --- what the presenter displayed ----------------------------------
    public IReadOnlyList<PileRange> Ranges { get; private set; } = Array.Empty<PileRange>();
    public IReadOnlyList<Pile> Piles { get; private set; } = Array.Empty<Pile>();
    public IReadOnlyList<JournalEntry> Journal { get; private set; } = Array.Empty<JournalEntry>();
    public int PilesRefreshed { get; private set; }

    public void ShowRanges(IReadOnlyList<PileRange> ranges) => Ranges = ranges;
    public void ShowPiles(IReadOnlyList<Pile> piles) => Piles = piles;
    public void ShowJournal(IReadOnlyList<JournalEntry> entries) => Journal = entries;
    public void RefreshPiles() => PilesRefreshed++;

    public IReadOnlyList<string> Sites { get; private set; } = Array.Empty<string>();
    public string CurrentSite { get; private set; } = "";
    public void ShowSites(IReadOnlyList<string> sites, string current)
    {
        Sites = sites;
        CurrentSite = current;
    }

    // --- what the presenter asked --------------------------------------
    public List<string> Errors { get; } = new();
    public List<string> Infos { get; } = new();
    public List<string> Questions { get; } = new();
    public List<string> Opened { get; } = new();

    /// <summary>How the user answers a confirmation; every question by default: yes.</summary>
    public Func<string, string, bool> AnswerConfirm { get; set; } = (_, _) => true;

    /// <summary>Paths the file dialogs return; null means the user cancelled.</summary>
    public string? SchedulePath { get; set; }
    public string? MetrykiPath { get; set; }
    public string? ProjectPath { get; set; }
    public string? SaveProjectPath { get; set; }
    public string? SuggestedMetrykiName { get; private set; }

    public string? AskForSchedule() => SchedulePath;
    public string? AskWhereToSaveMetryki(string suggestedName)
    {
        SuggestedMetrykiName = suggestedName;
        return MetrykiPath;
    }
    public string? AskForProject() => ProjectPath;
    public string? SuggestedProjectName { get; private set; }
    public string? AskWhereToSaveProject(string suggestedName)
    {
        SuggestedProjectName = suggestedName;
        return SaveProjectPath;
    }

    /// <summary>What the user types into the next text prompts, in order; null = Cancel.</summary>
    public Queue<string?> TypedTexts { get; } = new();
    public List<string> Prompts { get; } = new();
    public string? AskForText(string title, string prompt, string initial)
    {
        Prompts.Add($"{title}: {initial}");
        return TypedTexts.Count > 0 ? TypedTexts.Dequeue() : null;
    }

    public void ShowError(string title, string message) => Errors.Add($"{title}: {message}");
    public void ShowInfo(string title, string message) => Infos.Add($"{title}: {message}");

    public bool Confirm(string title, string question)
    {
        Questions.Add($"{title}: {question}");
        return AnswerConfirm(title, question);
    }

    public void OpenExternally(string path) => Opened.Add(path);

    // --- pressing the buttons ------------------------------------------
    public event EventHandler? LoadScheduleRequested;
    public event EventHandler? AddDayRequested;
    public event EventHandler? AddSelectedPilesRequested;
    public event EventHandler? RemoveDayRequested;
    public event EventHandler? GenerateRequested;
    public event EventHandler? GenerateSelectedDaysRequested;
    public event EventHandler? OpenOutputRequested;
    public event EventHandler? SettingsChanged;
    public event EventHandler? ConcreteFactorChanged;
    public event EventHandler? PilesPerPageChanged;
    public event EventHandler? ConcretePlantChanged;
    public event EventHandler<PileEdited>? PileEdited;
    public event EventHandler<DayFactorEdited>? DayFactorEdited;
    public event EventHandler<DayConcreteEdited>? DayConcreteEdited;
    public event EventHandler? ConcreteModeChanged;
    public event EventHandler? ThemeChanged;
    public event EventHandler? JournalOrderChanged;
    public void ChangeJournalOrder(bool newestFirst)
    {
        JournalNewestFirst = newestFirst;
        JournalOrderChanged?.Invoke(this, EventArgs.Empty);
    }
    public void ChangeTheme(AppTheme theme)
    {
        Theme = theme;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }
    public event EventHandler? MissingMetrykiRequested;
    public event EventHandler? FooterImageRequested;
    public event EventHandler? FooterImageCleared;
    public event EventHandler<string>? SiteSelected;
    public event EventHandler? NewSiteRequested;
    public event EventHandler? RenameSiteRequested;
    public event EventHandler? OpenProjectRequested;
    public event EventHandler? SaveProjectAsRequested;
    public event EventHandler? ShowDataFolderRequested;
    public event EventHandler? ViewClosing;

    public void ClickLoadSchedule() => LoadScheduleRequested?.Invoke(this, EventArgs.Empty);
    public void ClickAddDay() => AddDayRequested?.Invoke(this, EventArgs.Empty);
    public void ClickAddSelected() => AddSelectedPilesRequested?.Invoke(this, EventArgs.Empty);
    public void ClickRemoveDay() => RemoveDayRequested?.Invoke(this, EventArgs.Empty);
    public void ClickGenerate() => GenerateRequested?.Invoke(this, EventArgs.Empty);
    public void ClickGenerateSelectedDays(params DateTime[] days)
    {
        SelectedJournalDays = days;
        GenerateSelectedDaysRequested?.Invoke(this, EventArgs.Empty);
    }
    public void ClickOpenOutput() => OpenOutputRequested?.Invoke(this, EventArgs.Empty);
    public void ChangeConcreteFactor(double value)
    {
        ConcreteFactor = value;
        ConcreteFactorChanged?.Invoke(this, EventArgs.Empty);
    }
    public void ChangePilesPerPage(int value)
    {
        PilesPerPage = value;
        PilesPerPageChanged?.Invoke(this, EventArgs.Empty);
    }
    public void ChangeSiteDetails(string budowa)
    {
        Budowa = budowa;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    public void ChangeOutputFormat(MetrykaFormat format)
    {
        OutputFormat = format;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    public void ChangeConcretePlant(string value)
    {
        Betoniarnia = value;
        ConcretePlantChanged?.Invoke(this, EventArgs.Empty);
    }
    public void EditPile(int rowIndex, string property) => PileEdited?.Invoke(this, new PileEdited(rowIndex, property));
    public void EditDayFactor(DateTime date, double factor) => DayFactorEdited?.Invoke(this, new DayFactorEdited(date, factor));
    public void EditDayConcrete(DateTime date, double? total) => DayConcreteEdited?.Invoke(this, new DayConcreteEdited(date, total));
    public void ChangeConcreteMode(ConcreteMode mode)
    {
        ConcreteMode = mode;
        ConcreteModeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Logs a day with the concrete used typed next to the pile numbers.</summary>
    public void LogDay(DateTime date, string piles, double concreteUsed)
    {
        JournalConcreteUsed = concreteUsed;
        LogDay(date, piles);
    }
    public void ClickMissingMetryki() => MissingMetrykiRequested?.Invoke(this, EventArgs.Empty);
    public void ChooseFooterImage(string? path)
    {
        ImagePath = path;
        FooterImageRequested?.Invoke(this, EventArgs.Empty);
    }
    public void ClickClearFooterImage() => FooterImageCleared?.Invoke(this, EventArgs.Empty);
    public void ChangeFooter(string text, FooterPosition position)
    {
        FooterText = text;
        FooterImagePosition = position;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    public void PickSite(string name) => SiteSelected?.Invoke(this, name);
    public void AddSite(params string?[] typed)
    {
        foreach (var t in typed) TypedTexts.Enqueue(t);
        NewSiteRequested?.Invoke(this, EventArgs.Empty);
    }
    public void ClickRenameSite(params string?[] typed)
    {
        foreach (var t in typed) TypedTexts.Enqueue(t);
        RenameSiteRequested?.Invoke(this, EventArgs.Empty);
    }
    public void ClickShowDataFolder() => ShowDataFolderRequested?.Invoke(this, EventArgs.Empty);
    public void ClickOpenProject() => OpenProjectRequested?.Invoke(this, EventArgs.Empty);
    public void ClickSaveProjectAs() => SaveProjectAsRequested?.Invoke(this, EventArgs.Empty);
    public void CloseWindow() => ViewClosing?.Invoke(this, EventArgs.Empty);

    /// <summary>Types pile numbers into the journal box and presses the button.</summary>
    public void LogDay(DateTime date, string piles)
    {
        JournalDate = date;
        JournalPiles = piles;
        ClickAddDay();
    }
}

/// <summary>A project store that keeps everything in memory.</summary>
internal sealed class InMemoryProjectRepository : IProjectRepository
{
    private readonly Dictionary<string, string> _files = new();

    public string DefaultPath => @"pamiec\projekt.mpali";
    public string DataDirectory => "pamiec";
    public int Saves { get; private set; }

    private const string SitesPrefix = @"pamiec\budowy\";

    public IReadOnlyList<string> ListSites() => _files.Keys
        .Where(k => k.StartsWith(SitesPrefix))
        .Select(k => k[SitesPrefix.Length..^".mpali".Length])
        .OrderBy(n => n, StringComparer.Create(new System.Globalization.CultureInfo("pl-PL"), true))
        .ToList();

    public string SitePath(string name) => SitesPrefix + name + ".mpali";

    public void RenameSite(string name, string newName)
    {
        if (_files.ContainsKey(SitePath(newName))) throw new IOException("zajęta");
        _files[SitePath(newName)] = _files[SitePath(name)];
        _files.Remove(SitePath(name));
        if (LastSite == name) LastSite = newName;
    }

    public string? LastSite { get; set; }
    public string? Theme { get; set; }
    public int Backups { get; private set; }

    /// <summary>Set to make the next save fail, as a full or read-only disk would.</summary>
    public Exception? FailNextSaveWith { get; set; }

    public ProjectState? Load(string path)
        => _files.TryGetValue(path, out var json)
            ? System.Text.Json.JsonSerializer.Deserialize<ProjectState>(json)
            : null;

    public void Save(string path, ProjectState state)
    {
        if (FailNextSaveWith is { } failure)
        {
            FailNextSaveWith = null;
            throw failure;
        }

        // Round-trip through JSON so the fake behaves like the real store:
        // the presenter must not rely on holding the same object afterwards.
        _files[path] = System.Text.Json.JsonSerializer.Serialize(state);
        Saves++;
    }

    public void BackupOnce(string path) => Backups++;

    public bool Has(string path) => _files.ContainsKey(path);
}

/// <summary>A writer that records what it was asked to produce.</summary>
internal sealed class RecordingMetrykaWriter : IMetrykaWriter
{
    public string? Path { get; private set; }
    public IReadOnlyList<WorkDay> Days { get; private set; } = Array.Empty<WorkDay>();
    public MetrykaSettings? Settings { get; private set; }
    public int Calls { get; private set; }

    /// <summary>Set to make the write fail, as a locked output file would.</summary>
    public Exception? FailWith { get; set; }

    public void Write(string path, IReadOnlyList<WorkDay> days, MetrykaSettings settings)
    {
        if (FailWith is not null) throw FailWith;

        Path = path;
        Days = days;
        Settings = settings;
        Calls++;
    }
}

/// <summary>A reader that returns a schedule chosen by the test.</summary>
internal sealed class StubScheduleReader : IScheduleReader
{
    private readonly IReadOnlyList<PileRange> _ranges;
    private readonly Exception? _failure;

    public StubScheduleReader(params (int From, int To, double Diameter, double Length)[] ranges)
        => _ranges = ranges.Select(r => new PileRange
        {
            From = r.From, To = r.To, Diameter = r.Diameter, Length = r.Length, Reinforcement = "Brak"
        }).ToList();

    public StubScheduleReader(Exception failure)
    {
        _ranges = Array.Empty<PileRange>();
        _failure = failure;
    }

    public string? LastPath { get; private set; }

    public IReadOnlyList<PileRange> Read(string path)
    {
        LastPath = path;
        if (_failure is not null) throw _failure;
        return _ranges;
    }
}
