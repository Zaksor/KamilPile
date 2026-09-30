using MetrykiPali.Model;

namespace MetrykiPali.Presentation;

/// <summary>Which pile the user edited in the grid, and what changed.</summary>
public sealed record PileEdited(int RowIndex, string PropertyName);

/// <summary>The user typed a new concrete coefficient for a logged day.</summary>
public sealed record DayFactorEdited(DateTime Date, double Factor);

/// <summary>The window's colours; kept per computer, not per site.</summary>
public enum AppTheme
{
    Light,
    Dark
}

/// <summary>The user typed (or cleared) the concrete used on a logged day, m3.</summary>
public sealed record DayConcreteEdited(DateTime Date, double? Total);

/// <summary>
/// The main window, as the presenter sees it.
///
/// This is a passive view: it owns no logic, only the controls' contents and
/// the user's intentions. Everything that decides anything lives in
/// <see cref="MainPresenter"/>, which is why the app can be tested without
/// opening a window.
/// </summary>
public interface IMainView
{
    // --- what the window is showing ------------------------------------
    string SourcePath { get; set; }
    string Budowa { get; set; }
    string Wykonawca { get; set; }
    string Metoda { get; set; }
    string Betoniarnia { get; set; }
    double ConcreteFactor { get; set; }
    int PilesPerPage { get; set; }
    MetrykaFormat OutputFormat { get; set; }
    string FooterText { get; set; }
    FooterPosition FooterImagePosition { get; set; }
    string FooterImageLabel { get; set; }
    ConcreteMode ConcreteMode { get; set; }
    AppTheme Theme { get; set; }

    /// <summary>Concrete used on the day being entered, m3; null when left empty.</summary>
    double? JournalConcreteUsed { get; set; }
    string MissingMetrykiText { get; set; }
    DateTime JournalDate { get; set; }
    string JournalPiles { get; set; }
    string StatusText { get; set; }
    bool CanGenerate { get; set; }
    bool CanOpenOutput { get; set; }
    bool Busy { get; set; }

    // --- what the user has picked --------------------------------------
    DateTime? SelectedJournalDay { get; }
    IReadOnlyList<DateTime> SelectedJournalDays { get; }
    IReadOnlyList<int> SelectedPileNumbers { get; }

    // --- filling the grids ---------------------------------------------
    void ShowRanges(IReadOnlyList<PileRange> ranges);
    void ShowPiles(IReadOnlyList<Pile> piles);
    void ShowJournal(IReadOnlyList<JournalEntry> entries);
    void ShowSites(IReadOnlyList<string> sites, string current);
    void ShowProgress(SiteProgress progress);
    void RefreshPiles();

    // --- talking to the user -------------------------------------------
    string? AskForSchedule();
    string? AskWhereToSaveMetryki(string suggestedName);
    string? AskForProject();
    string? AskWhereToSaveProject(string suggestedName);
    void ShowError(string title, string message);
    void ShowInfo(string title, string message);
    bool Confirm(string title, string question);
    string? AskForText(string title, string prompt, string initial);
    string? AskForImage();

    /// <summary>A longer piece of text the user may want to read through or copy.</summary>
    void ShowReport(string title, string text);

    /// <summary>Opens a file in its program, or a folder in Explorer.</summary>
    void OpenExternally(string path);

    // --- what the user asked for ---------------------------------------
    event EventHandler LoadScheduleRequested;
    event EventHandler AddDayRequested;
    event EventHandler AddSelectedPilesRequested;
    event EventHandler RemoveDayRequested;
    event EventHandler GenerateRequested;
    event EventHandler GenerateSelectedDaysRequested;
    event EventHandler OpenOutputRequested;
    event EventHandler SettingsChanged;
    event EventHandler ConcreteFactorChanged;
    event EventHandler PilesPerPageChanged;
    event EventHandler ConcretePlantChanged;
    event EventHandler<PileEdited> PileEdited;
    event EventHandler<DayFactorEdited> DayFactorEdited;
    event EventHandler<DayConcreteEdited> DayConcreteEdited;
    event EventHandler ConcreteModeChanged;
    event EventHandler ThemeChanged;
    event EventHandler MissingMetrykiRequested;
    event EventHandler FooterImageRequested;
    event EventHandler FooterImageCleared;
    event EventHandler<string> SiteSelected;
    event EventHandler NewSiteRequested;
    event EventHandler RenameSiteRequested;
    event EventHandler OpenProjectRequested;
    event EventHandler SaveProjectAsRequested;
    event EventHandler ShowDataFolderRequested;
    event EventHandler ViewClosing;
}
