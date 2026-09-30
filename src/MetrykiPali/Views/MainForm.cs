using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using MetrykiPali.Model;
using MetrykiPali.Presentation;
using MetrykiPali.Services;

namespace MetrykiPali.Views;

    /// <summary>
/// The window. A passive view: it builds the controls, exposes their contents
/// as properties, and raises an event when the user does something. It decides
/// nothing - <see cref="MainPresenter"/> does.
///
/// Layout, top to bottom: a bar with the site picker and the light/dark switch;
/// three cards - DANE BUDOWY, BETON, TABELKA Z PALAMI; the journal with its
/// entry row and tabs; and a bar with the counts and the generate buttons.
/// Everything is drawn in the colours of a <see cref="Palette"/>, so the whole
/// window switches between the light and the dark look.
/// </summary>
public sealed class MainForm : Form, IMainView
{
    private static readonly CultureInfo Polish = CultureInfo.GetCultureInfo("pl-PL");

    private BindingList<Pile> _piles = new();
    private readonly BindingList<JournalEntry> _journal = new();
    private Palette _palette = Palette.Light;

    // --- top bar
    private readonly Label _logo = new() { Text = "MP", Size = new Size(36, 36), TextAlign = ContentAlignment.MiddleCenter, Tag = "icon", Margin = new Padding(0, 12, 10, 0) };
    private readonly Label _appName = new() { Text = "Metryki pali", AutoSize = true, Margin = new Padding(0, 13, 0, 0) };
    private readonly Label _appSub = new() { Text = "dokumentacja powykonawcza", AutoSize = true, Margin = new Padding(0, 0, 0, 0), Tag = "muted" };
    private readonly SitePicker _site = new() { Margin = new Padding(24, 11, 10, 0) };
    private readonly FlatButton _btnNewSite = new("＋  Nowa budowa") { Margin = new Padding(0, 14, 0, 0) };
    private readonly FlatButton _btnMore = new("Więcej  ⌄", ButtonKind.Ghost) { Margin = new Padding(0, 14, 8, 0) };
    private readonly Segmented _theme = new("Jasny", "Ciemny") { Margin = new Padding(0, 15, 0, 0) };
    private readonly ContextMenuStrip _moreMenu = new();
    private string _currentSite = "";
    private string? _testMarker;

    // --- DANE BUDOWY
    private readonly InputBox _budowa = new();
    private readonly InputBox _wykonawca = new();
    private readonly InputBox _metoda = new();
    private readonly InputBox _footer = new("tekst w lewym dolnym rogu każdej strony");
    private readonly Label _logoName = new() { AutoEllipsis = true, Size = new Size(78, 28), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(3, 4, 0, 0) };
    private readonly FlatButton _btnLogo = new("＋ logo", ButtonKind.Ghost) { Margin = new Padding(0, 2, 0, 0), Height = 30 };
    private readonly FlatButton _btnLogoClear = new("Usuń", ButtonKind.Ghost) { Margin = new Padding(0, 2, 4, 0), Height = 30 };
    private readonly Segmented _logoPosition = new("lewo", "środek", "prawo") { Margin = new Padding(0, 2, 0, 0) };

    // --- BETON
    private readonly Segmented _concreteMode = new("współczynnik", "ilość betonu") { Margin = new Padding(3, 3, 3, 3) };
    private readonly NumberBox _factor = new() { Minimum = 1.00m, Maximum = 3.00m, Increment = 0.01m, Decimals = 2 };
    private readonly NumberBox _used = new("dla wpisywanego dnia") { Minimum = 0.01m, Maximum = (decimal)MainPresenter.MaxDayConcrete, Increment = 0.5m, Decimals = 2, AllowEmpty = true };
    private readonly InputBox _plant = new();
    private readonly Label _factorLabel = new() { Text = "Współczynnik" };
    private readonly Label _usedLabel = new() { Text = "Ilość betonu [m³]" };

    // --- TABELKA Z PALAMI
    private readonly FlatButton _btnLoad = new("Wczytaj tabelkę…");
    private readonly Label _fileIcon = new() { Size = new Size(38, 42), TextAlign = ContentAlignment.MiddleCenter, Tag = "icon", Margin = new Padding(0, 2, 10, 0) };
    private readonly Label _fileName = new() { AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft };
    private readonly Label _fileDetails = new() { AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft, Tag = "muted" };
    private readonly ProgressLine _pileProgress = new() { Dock = DockStyle.Fill };
    private readonly ProgressLine _concreteProgress = new() { Dock = DockStyle.Fill };
    private readonly Label _concreteBuilt = new() { Dock = DockStyle.Fill, Tag = "muted", TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolTip _tips = new();
    private string _sourcePath = "";

    // --- journal
    private readonly TabStrip _tabs = new("Dziennik robót", "Pale", "Zakresy z tabelki") { Dock = DockStyle.Fill };
    private readonly FlatButton _btnRemoveDay = new("Usuń zaznaczony dzień", ButtonKind.Ghost) { Dock = DockStyle.Right };
    private readonly DateBox _day = new() { Dock = DockStyle.Fill };
    private readonly InputBox _dayPiles = new("Pale, np. 1-10, 25, 30-33   ·   Enter dodaje") { Dock = DockStyle.Fill };
    private readonly FlatButton _btnAddDay = new("Dodaj do dziennika", ButtonKind.Primary) { Margin = new Padding(6, 3, 0, 3) };
    private readonly FlatButton _btnAddSelected = new("Dodaj zaznaczone z listy pali") { Margin = new Padding(8, 3, 0, 3) };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Tag = "muted", TextAlign = ContentAlignment.MiddleLeft };
    private readonly DataGridView _gridJournal = NewGrid();
    private readonly DataGridView _gridRanges = NewGrid();
    private readonly DataGridView _gridPiles = NewGrid();

    // --- bottom bar
    private readonly Chip _chipLogged = new("W dzienniku");
    private readonly Chip _chipUndated = new("Bez daty");
    private readonly Chip _chipPages = new("Stron");
    private readonly LinkLabel _missing = new() { AutoSize = true, Margin = new Padding(6, 5, 0, 0), LinkBehavior = LinkBehavior.HoverUnderline };
    private readonly NumberBox _perPage = new() { Minimum = 1, Maximum = 12, Increment = 1, Width = 50, Margin = new Padding(6, 0, 12, 0) };
    private readonly Segmented _format = new("Excel", "PDF") { Margin = new Padding(0, 1, 10, 0) };
    private readonly FlatButton _btnOpen = new("Otwórz plik", ButtonKind.Ghost) { Enabled = false, Margin = new Padding(0, 0, 6, 0) };
    private readonly FlatButton _btnGenerateSelected = new("Generuj zaznaczone dni") { Enabled = false, Margin = new Padding(0, 0, 8, 0) };
    private readonly FlatButton _btnGenerate = new("Generuj wszystkie dni", ButtonKind.Primary) { Enabled = false, Margin = Padding.Empty };

    private readonly List<Panel> _surfaces = new();      // panels painted in the card colour
    private readonly List<Panel> _bars = new();          // the top and bottom bars
    private readonly List<Control> _windowSurfaces = new(); // panels painted in the window colour (gaps around cards)

    public MainForm()
    {
        Text = "Metryki pali";
        Width = 1280;
        Height = 900;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1120, 780);
        Font = new Font("Segoe UI", 9f);

        BuildUi();
        WireEvents();
        ApplyTheme(Palette.Light);
    }

    // ------------------------------------------------------- IMainView state

    string IMainView.SourcePath
    {
        get => _sourcePath;
        set
        {
            _sourcePath = value;
            var ext = Path.GetExtension(value).TrimStart('.').ToUpperInvariant();
            _fileIcon.Text = value.Length == 0 ? "—" : ext is "XLSX" or "XLSM" ? "XLS" : ext;
            _fileName.Text = value.Length == 0 ? "Nie wczytano tabelki" : Path.GetFileName(value);
            _tips.SetToolTip(_fileName, value);
        }
    }

    string IMainView.Budowa { get => _budowa.Box.Text; set => _budowa.Box.Text = value; }
    string IMainView.Wykonawca { get => _wykonawca.Box.Text; set => _wykonawca.Box.Text = value; }
    string IMainView.Metoda { get => _metoda.Box.Text; set => _metoda.Box.Text = value; }
    string IMainView.Betoniarnia { get => _plant.Box.Text; set => _plant.Box.Text = value; }
    string IMainView.JournalPiles { get => _dayPiles.Box.Text; set => _dayPiles.Box.Text = value; }
    string IMainView.StatusText { get => _status.Text; set => _status.Text = value; }
    string IMainView.FooterText { get => _footer.Box.Text; set => _footer.Box.Text = value; }
    // "Pale bez metryk: 957 — pokaż które" is shortened to fit beside the counts.
    string IMainView.MissingMetrykiText
    {
        get => _missingText;
        set
        {
            _missingText = value;
            var digits = new string(value.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
            _missing.Text = value.StartsWith("Pale bez") && digits.Length > 0 ? $"⚠ {digits} bez metryk — pokaż" : value;
            _missing.LinkColor = value.StartsWith("Pale bez") ? _palette.Danger : _palette.OkText;
        }
    }

    private string _missingText = "";

    string IMainView.FooterImageLabel
    {
        get => _logoName.Text;
        set
        {
            var none = value == "brak";
            _logoName.Text = none ? "bez logo" : value;
            _tips.SetToolTip(_logoName, none ? "" : value);
            _btnLogoClear.Visible = !none;
            _btnLogo.Text = none ? "＋ logo" : "Zmień";
        }
    }

    double IMainView.ConcreteFactor
    {
        get => (double)(_factor.Value ?? 1.30m);
        set => _factor.Value = (decimal)value;
    }

    int IMainView.PilesPerPage
    {
        get => (int)(_perPage.Value ?? 12);
        set => _perPage.Value = value;
    }

    double? IMainView.JournalConcreteUsed
    {
        get => _used.Value is { } v ? (double)v : null;
        set => _used.Value = value is { } v ? (decimal)v : null;
    }

    // Segment order follows the enums: Xlsx/Pdf, Left/Center/Right, Factor/Measured, Light/Dark.
    MetrykaFormat IMainView.OutputFormat
    {
        get => _format.SelectedIndex == 1 ? MetrykaFormat.Pdf : MetrykaFormat.Xlsx;
        set => _format.SelectedIndex = value == MetrykaFormat.Pdf ? 1 : 0;
    }

    FooterPosition IMainView.FooterImagePosition
    {
        get => (FooterPosition)_logoPosition.SelectedIndex;
        set => _logoPosition.SelectedIndex = (int)value;
    }

    ConcreteMode IMainView.ConcreteMode
    {
        get => _concreteMode.SelectedIndex == 1 ? ConcreteMode.Measured : ConcreteMode.Factor;
        set { _concreteMode.SelectedIndex = value == ConcreteMode.Measured ? 1 : 0; ShowConcreteFields(); }
    }

    AppTheme IMainView.Theme
    {
        get => _theme.SelectedIndex == 1 ? AppTheme.Dark : AppTheme.Light;
        set => _theme.SelectedIndex = value == AppTheme.Dark ? 1 : 0;
    }

    DateTime IMainView.JournalDate { get => _day.Value; set => _day.Value = value; }

    bool IMainView.CanGenerate
    {
        get => _btnGenerate.Enabled;
        set => _btnGenerate.Enabled = _btnGenerateSelected.Enabled = value;
    }

    bool IMainView.CanOpenOutput { get => _btnOpen.Enabled; set => _btnOpen.Enabled = value; }

    bool IMainView.Busy
    {
        get => Cursor == Cursors.WaitCursor;
        set
        {
            Cursor = value ? Cursors.WaitCursor : Cursors.Default;
            _btnLoad.Enabled = !value;
            _btnGenerate.Enabled = _btnGenerateSelected.Enabled = !value && _journal.Count > 0;
            Application.DoEvents();
        }
    }

    DateTime? IMainView.SelectedJournalDay
        => _gridJournal.CurrentRow?.DataBoundItem is JournalEntry row ? row.Data : null;

    IReadOnlyList<DateTime> IMainView.SelectedJournalDays => _gridJournal.SelectedRows
        .Cast<DataGridViewRow>()
        .Select(r => r.DataBoundItem)
        .OfType<JournalEntry>()
        .Select(e => e.Data)
        .OrderBy(d => d)
        .ToList();

    IReadOnlyList<int> IMainView.SelectedPileNumbers => _gridPiles.SelectedCells
        .Cast<DataGridViewCell>()
        .Select(c => c.RowIndex)
        .Distinct()
        .Where(i => i >= 0 && i < _piles.Count)
        .Select(i => _piles[i].Number)
        .ToList();

    // ------------------------------------------------------ IMainView events

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

    private void WireEvents()
    {
        // Site actions are handed over after the menu has closed.
        _site.SitePicked += (_, name) => BeginInvoke(() => SiteSelected?.Invoke(this, name));
        _site.NewSiteRequested += (_, _) => BeginInvoke(() => NewSiteRequested?.Invoke(this, EventArgs.Empty));
        _btnNewSite.Click += (_, _) => NewSiteRequested?.Invoke(this, EventArgs.Empty);
        _btnMore.Click += (_, _) => _moreMenu.Show(_btnMore, new Point(_btnMore.Width - _moreMenu.Width, _btnMore.Height + 2));
        _moreMenu.Items.Add("Zmień nazwę budowy…", null, (_, _) => RenameSiteRequested?.Invoke(this, EventArgs.Empty));
        _moreMenu.Items.Add("Dodaj budowę z pliku…", null, (_, _) => OpenProjectRequested?.Invoke(this, EventArgs.Empty));
        _moreMenu.Items.Add("Zapisz kopię budowy jako…", null, (_, _) => SaveProjectAsRequested?.Invoke(this, EventArgs.Empty));
        _moreMenu.Items.Add(new ToolStripSeparator());
        _moreMenu.Items.Add("Pokaż folder z danymi", null, (_, _) => ShowDataFolderRequested?.Invoke(this, EventArgs.Empty));

        _theme.SelectedIndexChanged += (_, _) =>
        {
            ApplyTheme(_theme.SelectedIndex == 1 ? Palette.Dark : Palette.Light);
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        };

        _btnLoad.Click += (_, _) => LoadScheduleRequested?.Invoke(this, EventArgs.Empty);
        _btnAddDay.Click += (_, _) => AddDayRequested?.Invoke(this, EventArgs.Empty);
        _btnAddSelected.Click += (_, _) => AddSelectedPilesRequested?.Invoke(this, EventArgs.Empty);
        _btnRemoveDay.Click += (_, _) => RemoveDayRequested?.Invoke(this, EventArgs.Empty);
        _btnGenerate.Click += (_, _) => GenerateRequested?.Invoke(this, EventArgs.Empty);
        _btnGenerateSelected.Click += (_, _) => GenerateSelectedDaysRequested?.Invoke(this, EventArgs.Empty);
        _btnOpen.Click += (_, _) => OpenOutputRequested?.Invoke(this, EventArgs.Empty);
        _missing.LinkClicked += (_, _) => MissingMetrykiRequested?.Invoke(this, EventArgs.Empty);

        _factor.ValueChanged += (_, _) => ConcreteFactorChanged?.Invoke(this, EventArgs.Empty);
        _perPage.ValueChanged += (_, _) => PilesPerPageChanged?.Invoke(this, EventArgs.Empty);
        _plant.Box.TextChanged += (_, _) => ConcretePlantChanged?.Invoke(this, EventArgs.Empty);
        _concreteMode.SelectedIndexChanged += (_, _) =>
        {
            ShowConcreteFields();
            ConcreteModeChanged?.Invoke(this, EventArgs.Empty);
        };

        _format.SelectedIndexChanged += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
        _logoPosition.SelectedIndexChanged += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
        _btnLogo.Click += (_, _) => FooterImageRequested?.Invoke(this, EventArgs.Empty);
        _btnLogoClear.Click += (_, _) => FooterImageCleared?.Invoke(this, EventArgs.Empty);
        foreach (var box in new[] { _budowa, _wykonawca, _metoda, _footer })
            box.Box.Leave += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);

        _dayPiles.Box.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            AddDayRequested?.Invoke(this, EventArgs.Empty);
        };

        _tabs.SelectedIndexChanged += (_, _) =>
        {
            _gridJournal.Visible = _tabs.SelectedIndex == 0;
            _gridPiles.Visible = _tabs.SelectedIndex == 1;
            _gridRanges.Visible = _tabs.SelectedIndex == 2;
            _btnRemoveDay.Visible = _tabs.SelectedIndex == 0;
        };

        _gridPiles.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            PileEdited?.Invoke(this, new PileEdited(e.RowIndex, _gridPiles.Columns[e.ColumnIndex].DataPropertyName));
        };

        _gridJournal.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (_gridJournal.Rows[e.RowIndex].DataBoundItem is not JournalEntry entry) return;

            // The presenter answers by refilling this grid, which cannot happen
            // while the grid is still inside its own edit - so hand it over after.
            switch (_gridJournal.Columns[e.ColumnIndex].DataPropertyName)
            {
                case nameof(JournalEntry.Wsp):
                    var factor = new DayFactorEdited(entry.Data, entry.Wsp);
                    BeginInvoke(() => DayFactorEdited?.Invoke(this, factor));
                    break;
                case nameof(JournalEntry.Zuzyto):
                    var used = new DayConcreteEdited(entry.Data, entry.Zuzyto);
                    BeginInvoke(() => DayConcreteEdited?.Invoke(this, used));
                    break;
            }
        };

        _gridJournal.DataError += (_, e) =>
        {
            e.Cancel = true;
            MessageBox.Show(this, "Wpisz liczbę, np. 1,25.", "Błędna liczba", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        _gridJournal.CellPainting += PaintMetrykiBadge;
        foreach (var grid in new[] { _gridJournal, _gridPiles, _gridRanges })
        {
            grid.CellPainting += PaintCell;
            grid.RowPrePaint += (_, e) => e.PaintParts &= ~DataGridViewPaintParts.Focus;   // the whole-row frame of the current day
        }

        FormClosing += (_, _) => ViewClosing?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Only the field that the chosen method uses can be typed in: the
    /// coefficient for "współczynnik", the concrete used for "ilość betonu".
    /// </summary>
    private void ShowConcreteFields()
    {
        var measured = _concreteMode.SelectedIndex == 1;
        _factor.Enabled = !measured;
        _used.Enabled = measured;
        _factorLabel.ForeColor = measured ? _palette.Muted : _palette.Text;
        _usedLabel.ForeColor = measured ? _palette.Text : _palette.Muted;
    }

    // ----------------------------------------------------- IMainView display

    public void ShowRanges(IReadOnlyList<PileRange> ranges)
    {
        _gridRanges.DataSource = new BindingList<PileRange>(ranges.ToList());
        SetHeaders(_gridRanges, new()
        {
            [nameof(PileRange.From)] = "Numer od",
            [nameof(PileRange.To)] = "Numer do",
            [nameof(PileRange.Diameter)] = "Średnica [m]",
            [nameof(PileRange.Length)] = "Długość [m]",
            [nameof(PileRange.Reinforcement)] = "Zbrojenie",
            [nameof(PileRange.Count)] = "Ilość pali"
        });
    }

    public void ShowPiles(IReadOnlyList<Pile> piles)
    {
        _piles = piles as BindingList<Pile> ?? new BindingList<Pile>(piles.ToList());
        _gridPiles.DataSource = _piles;
        SetHeaders(_gridPiles, new()
        {
            [nameof(Pile.Number)] = "Numer pala",
            [nameof(Pile.Diameter)] = "Średnica [m]",
            [nameof(Pile.DesignLength)] = "Dł. wg projektu [m]",
            [nameof(Pile.ActualLength)] = "Dł. wykonana [m]",
            [nameof(Pile.Concrete)] = "Beton [m³]",
            [nameof(Pile.ConcretePlant)] = "Betoniarnia",
            [nameof(Pile.Reinforcement)] = "Zbrojenie",
            [nameof(Pile.Executed)] = "Data wykonania",
            [nameof(Pile.ConcreteFactor)] = "Wsp. betonu",
            [nameof(Pile.MetrykaGenerated)] = "Metryka z dnia",
            [nameof(Pile.DayConcreteUsed)] = "Zużyto w dniu [m³]"
        });

        foreach (DataGridViewColumn column in _gridPiles.Columns)
            column.ReadOnly = column.DataPropertyName is not (nameof(Pile.ActualLength) or nameof(Pile.Diameter));
        if (_gridPiles.Columns[nameof(Pile.Executed)] is { } executed) executed.DefaultCellStyle.Format = "dd.MM.yyyy";
        if (_gridPiles.Columns[nameof(Pile.MetrykaGenerated)] is { } generated) generated.DefaultCellStyle.Format = "dd.MM.yyyy";
        if (_gridPiles.Columns[nameof(Pile.ConcreteFactor)] is { } factor) factor.DefaultCellStyle.Format = "0.00";
        if (_gridPiles.Columns[nameof(Pile.Concrete)] is { } concrete) concrete.DefaultCellStyle.Format = "0.00";
        StyleEditableColumns();
    }

    public void ShowJournal(IReadOnlyList<JournalEntry> entries)
    {
        _journal.RaiseListChangedEvents = false;
        _journal.Clear();
        foreach (var entry in entries) _journal.Add(entry);
        _journal.RaiseListChangedEvents = true;
        _journal.ResetBindings();

        if (_gridJournal.DataSource is null) _gridJournal.DataSource = _journal;

        SetHeaders(_gridJournal, new()
        {
            [nameof(JournalEntry.Data)] = "Data",
            [nameof(JournalEntry.Pale)] = "Pale",
            [nameof(JournalEntry.Ilosc)] = "Ilość",
            [nameof(JournalEntry.Beton)] = "Beton [m³]",
            [nameof(JournalEntry.Wsp)] = "Wsp. ✎",
            [nameof(JournalEntry.Zuzyto)] = "Zużyto [m³] ✎",
            [nameof(JournalEntry.Strony)] = "Stron",
            [nameof(JournalEntry.Metryki)] = "Metryki"
        });

        foreach (DataGridViewColumn column in _gridJournal.Columns)
        {
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            column.ReadOnly = column.DataPropertyName is not (nameof(JournalEntry.Wsp) or nameof(JournalEntry.Zuzyto));
        }

        Column(_gridJournal, nameof(JournalEntry.Data), c => { c.DefaultCellStyle.Format = "dd.MM.yyyy"; c.FillWeight = 70; });
        Column(_gridJournal, nameof(JournalEntry.Pale), c => c.FillWeight = 190);
        Column(_gridJournal, nameof(JournalEntry.Ilosc), c => { c.FillWeight = 45; AlignRight(c); });
        Column(_gridJournal, nameof(JournalEntry.Beton), c => { c.DefaultCellStyle.Format = "0.00"; c.FillWeight = 70; AlignRight(c); });
        Column(_gridJournal, nameof(JournalEntry.Wsp), c =>
        {
            c.DefaultCellStyle.Format = "0.00"; c.FillWeight = 55; AlignRight(c);
            c.ToolTipText = "Współczynnik dnia — kliknij dwukrotnie, aby zmienić. Beton pali z tego dnia przeliczy się od razu.";
        });
        Column(_gridJournal, nameof(JournalEntry.Zuzyto), c =>
        {
            c.DefaultCellStyle.Format = "0.00"; c.DefaultCellStyle.NullValue = "—"; c.DefaultCellStyle.DataSourceNullValue = null;
            c.FillWeight = 80; AlignRight(c);
            c.ToolTipText = "Ile betonu zużyto tego dnia (np. z WZ). Program rozdzieli go na pale według ich objętości. " +
                            "Wyczyść komórkę, aby wrócić do liczenia ze współczynnika.";
        });
        Column(_gridJournal, nameof(JournalEntry.Strony), c => { c.FillWeight = 45; AlignRight(c); });
        Column(_gridJournal, nameof(JournalEntry.Metryki), c => c.FillWeight = 90);
        StyleEditableColumns();
    }

    public void RefreshPiles() => _gridPiles.Refresh();

    public void ShowSites(IReadOnlyList<string> sites, string current)
    {
        _currentSite = current;
        _site.Show(sites, current);
        Text = $"Metryki pali — {current}" + (_testMarker ?? "");
    }

    public void ShowProgress(SiteProgress p)
    {
        _chipLogged.Value = p.Logged.ToString(Polish);
        _chipUndated.Value = p.Undated.ToString(Polish);
        _chipPages.Value = p.Pages.ToString(Polish);

        _tabs.SetText(0, $"Dziennik robót ({p.Days})");
        _tabs.SetText(1, $"Pale ({p.Piles})");
        _tabs.SetText(2, $"Zakresy z tabelki ({p.Ranges})");

        var folder = Path.GetDirectoryName(_sourcePath);
        _fileDetails.Text = p.Piles == 0 ? "Wczytaj tabelkę od projektanta (.xls, .xlsx, .csv, .pdf)."
            : $"{(string.IsNullOrEmpty(folder) ? "" : folder + "  ·  ")}{p.Ranges} {Ranges(p.Ranges)}  ·  {p.Piles} pali";

        _pileProgress.Set("Pale w dzienniku", $"{p.Logged} z {p.Piles}", p.Piles == 0 ? 0 : p.Logged / (double)p.Piles);
        _concreteProgress.Set("Beton — objętość teoretyczna",
            $"{p.VolumeLogged.ToString("N1", Polish)} z {p.VolumeAll.ToString("N1", Polish)} m³",
            p.VolumeAll <= 0 ? 0 : p.VolumeLogged / p.VolumeAll);
        _concreteBuilt.Text = p.Logged == 0 ? "" : $"Wbudowano dotąd: {p.ConcreteLogged.ToString("N2", Polish)} m³ betonu";
    }

    private static string Ranges(int n)
        => n == 1 ? "zakres" : n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? "zakresy" : "zakresów";

    /// <summary>Text kept at the end of the title whatever site is open (the test-copy marker).</summary>
    public void SetTitleSuffix(string suffix)
    {
        _testMarker = suffix;
        Text += suffix;
    }

    // ------------------------------------------------- IMainView interaction

    public string? AskForSchedule()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Wybierz tabelkę z palami",
            Filter = "Wszystkie obsługiwane (*.xlsx;*.xls;*.xlsm;*.csv;*.pdf)|*.xlsx;*.xls;*.xlsm;*.csv;*.pdf|" +
                     "Excel (*.xlsx;*.xls;*.xlsm)|*.xlsx;*.xls;*.xlsm|CSV (*.csv)|*.csv|PDF (*.pdf)|*.pdf"
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    public string? AskWhereToSaveMetryki(string suggestedName)
    {
        var pdf = MetrykaFileWriter.IsPdf(suggestedName);
        using var dialog = new SaveFileDialog
        {
            Title = pdf ? "Zapisz metryki pali jako PDF" : "Zapisz metryki pali jako Excel",
            Filter = pdf ? "Dokument PDF (*.pdf)|*.pdf" : "Skoroszyt Excel (*.xlsx)|*.xlsx",
            FileName = suggestedName
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    public string? AskForProject()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Dodaj budowę z pliku",
            Filter = $"Budowa — metryki pali (*{JsonProjectRepository.FileExtension})|*{JsonProjectRepository.FileExtension}"
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    public string? AskWhereToSaveProject(string suggestedName)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Zapisz kopię budowy jako",
            Filter = $"Budowa — metryki pali (*{JsonProjectRepository.FileExtension})|*{JsonProjectRepository.FileExtension}",
            FileName = suggestedName
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    public string? AskForImage()
    {
        var patterns = string.Join(";", FooterImage.Extensions.Select(e => "*" + e));
        using var dialog = new OpenFileDialog
        {
            Title = "Wybierz obraz do stopki (np. logo firmy)",
            Filter = $"Obrazy ({patterns})|{patterns}"
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    public void ShowReport(string title, string text)
    {
        using var form = Dialog(title, new Size(720, 480));
        form.FormBorderStyle = FormBorderStyle.Sizable;
        form.MinimumSize = new Size(420, 260);
        var box = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None, Font = new Font("Consolas", 10f),
            Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n"),
            BackColor = _palette.Card, ForeColor = _palette.Text
        };
        var close = new FlatButton("Zamknij", ButtonKind.Primary) { DialogResult = DialogResult.OK, Dock = DockStyle.Right };
        close.ApplyTheme(_palette);
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(16, 10, 16, 10), BackColor = _palette.Card };
        bottom.Controls.Add(close);
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 14, 8, 0), BackColor = _palette.Card };
        body.Controls.Add(box);
        form.Controls.Add(body);
        form.Controls.Add(bottom);
        form.AcceptButton = close;
        form.CancelButton = close;
        form.Shown += (_, _) => { box.SelectionStart = 0; box.SelectionLength = 0; NativeTheme.Scrollbars(box, _palette.IsDark); };
        form.ShowDialog(this);
    }

    public string? AskForText(string title, string prompt, string initial)
    {
        using var form = Dialog(title, new Size(480, 170));
        var label = new Label { Text = prompt, Left = 18, Top = 16, Width = 444, Height = 36, ForeColor = _palette.Text, BackColor = _palette.Card };
        var box = new InputBox { Left = 18, Top = 58, Width = 444 };
        box.Box.Text = initial;
        box.ApplyTheme(_palette);
        var ok = new FlatButton("OK", ButtonKind.Primary) { DialogResult = DialogResult.OK, Left = 286, Top = 114, Width = 84 };
        var cancel = new FlatButton("Anuluj") { DialogResult = DialogResult.Cancel, Left = 378, Top = 114, Width = 84 };
        ok.ApplyTheme(_palette);
        cancel.ApplyTheme(_palette);
        form.Controls.AddRange(new Control[] { label, box, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.Shown += (_, _) => { box.Box.Focus(); box.Box.SelectAll(); };

        return form.ShowDialog(this) == DialogResult.OK ? box.Box.Text : null;
    }

    private Form Dialog(string title, Size size)
    {
        var form = new Form
        {
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, ClientSize = size,
            BackColor = _palette.Card, ForeColor = _palette.Text, Font = Font
        };
        form.HandleCreated += (_, _) => NativeTheme.TitleBar(form, _palette.IsDark);
        return form;
    }

    public void ShowError(string title, string message)
        => MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    public void ShowInfo(string title, string message)
        => MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);

    public bool Confirm(string title, string question)
        => MessageBox.Show(this, question, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    public void OpenExternally(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    // --------------------------------------------------------------- layout

    private static DataGridView NewGrid() => new QuietGrid()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.CellSelect,
        RowHeadersVisible = false,
        BorderStyle = BorderStyle.None,
        CellBorderStyle = DataGridViewCellBorderStyle.None,          // row lines are drawn by RowLine
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
        EnableHeadersVisualStyles = false,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        ColumnHeadersHeight = 34,
        RowTemplate = { Height = 34 }
    };

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty, Padding = Padding.Empty };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 262));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        root.Controls.Add(BuildTopBar(), 0, 0);
        root.Controls.Add(BuildCards(), 0, 1);
        root.Controls.Add(BuildJournal(), 0, 2);
        root.Controls.Add(BuildBottomBar(), 0, 3);
        Controls.Add(root);
    }

    private Control BuildTopBar()
    {
        var bar = new BarPanel(top: false) { Dock = DockStyle.Fill, Padding = new Padding(20, 0, 20, 1), Margin = Padding.Empty };
        _bars.Add(bar);

        _logo.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
        _appName.Font = new Font(Font.FontFamily, 11.5f, FontStyle.Bold);
        _appSub.Font = new Font(Font.FontFamily, 8f);
        var names = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 0) };
        names.Controls.Add(_appName);
        names.Controls.Add(_appSub);

        var left = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false };
        left.Controls.AddRange(new Control[] { _logo, names, _site, _btnNewSite });
        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        right.Controls.AddRange(new Control[] { _btnMore, _theme });
        _tips.SetToolTip(_theme, "Wygląd okna: jasny albo ciemny");

        bar.Controls.Add(right);
        bar.Controls.Add(left);
        return bar;
    }

    private Control BuildCards()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(20, 16, 20, 0), Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));

        // DANE BUDOWY
        var site = new Card("Dane budowy") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 0) };
        var siteFields = Fields(site.Body, 104, 5);
        AddField(siteFields, 0, "Budowa", _budowa);
        AddField(siteFields, 1, "Wykonawca", _wykonawca);
        AddField(siteFields, 2, "Metoda", _metoda);
        AddField(siteFields, 3, "Stopka", _footer);
        var logo = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 1, 0, 0) };
        logo.Controls.AddRange(new Control[] { _logoName, _btnLogo, _btnLogoClear, _logoPosition });
        _surfaces.Add(logo);
        _tips.SetToolTip(_logoPosition, "Gdzie w stopce ma stać logo");
        AddField(siteFields, 4, "Logo w stopce", logo);

        // BETON
        var concrete = new Card("Beton") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 0) };
        var concreteFields = Fields(concrete.Body, 122, 4);
        _concreteMode.Dock = DockStyle.None;
        _concreteMode.Width = 216;
        AddField(concreteFields, 0, "Sposób liczenia", _concreteMode);
        AddField(concreteFields, 1, _factorLabel, _factor);
        AddField(concreteFields, 2, _usedLabel, _used);
        AddField(concreteFields, 3, "Betoniarnia", _plant);
        _tips.SetToolTip(_factor, "Współczynnik dla nowo wpisywanych dni (objętość teoretyczna × współczynnik)");
        _tips.SetToolTip(_used, "Ile betonu zużyto w dniu, który teraz wpisujesz — program rozdzieli go na pale");

        // TABELKA Z PALAMI
        var schedule = new Card("Tabelka z palami", _btnLoad) { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, Margin = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _fileIcon.Font = new Font(Font.FontFamily, 8.5f, FontStyle.Bold);
        _fileName.Font = new Font(Font.FontFamily, 10f, FontStyle.Bold);
        body.Controls.Add(_fileIcon, 0, 0);
        body.SetRowSpan(_fileIcon, 2);
        body.Controls.Add(_fileName, 1, 0);
        body.Controls.Add(_fileDetails, 1, 1);
        body.Controls.Add(_pileProgress, 0, 2);
        body.SetColumnSpan(_pileProgress, 2);
        body.Controls.Add(_concreteProgress, 0, 3);
        body.SetColumnSpan(_concreteProgress, 2);
        body.Controls.Add(_concreteBuilt, 0, 4);
        body.SetColumnSpan(_concreteBuilt, 2);
        schedule.Body.Controls.Add(body);
        _surfaces.Add(body);
        _tips.SetToolTip(_concreteProgress, "Czysta objętość geometryczna pali z tabelki (średnica i długość), bez współczynników");

        row.Controls.Add(site, 0, 0);
        row.Controls.Add(concrete, 1, 0);
        row.Controls.Add(schedule, 2, 0);
        _windowSurfaces.Add(row);
        return row;
    }

    private Control BuildJournal()
    {
        var holder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 14, 20, 14) };
        var card = new Card("") { Dock = DockStyle.Fill };
        card.Controls[1].Height = 0;                      // no caption: the tabs are the heading

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var tabRow = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        tabRow.Controls.Add(_tabs);
        tabRow.Controls.Add(_btnRemoveDay);
        _surfaces.Add(tabRow);

        var entry = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(0, 6, 0, 0) };
        entry.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        entry.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        entry.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        entry.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _day.Margin = new Padding(0, 3, 6, 3);
        entry.Controls.Add(_day, 0, 0);
        entry.Controls.Add(_dayPiles, 1, 0);
        entry.Controls.Add(_btnAddDay, 2, 0);
        entry.Controls.Add(_btnAddSelected, 3, 0);
        _surfaces.Add(entry);

        var grids = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };
        grids.Controls.Add(_gridJournal);
        grids.Controls.Add(_gridPiles);
        grids.Controls.Add(_gridRanges);
        _gridPiles.Visible = _gridRanges.Visible = false;
        _gridJournal.SelectionMode = DataGridViewSelectionMode.FullRowSelect;   // whole days, several with Ctrl/Shift
        _gridJournal.MultiSelect = true;
        _surfaces.Add(grids);

        body.Controls.Add(tabRow, 0, 0);
        body.Controls.Add(entry, 0, 1);
        body.Controls.Add(_status, 0, 2);
        body.Controls.Add(grids, 0, 3);
        card.Body.Controls.Add(body);
        _surfaces.Add(body);

        holder.Controls.Add(card);
        _windowSurfaces.Add(holder);
        return holder;
    }

    private Control BuildBottomBar()
    {
        var bar = new BarPanel(top: true) { Dock = DockStyle.Fill, Padding = new Padding(20, 13, 20, 0), Margin = Padding.Empty };
        _bars.Add(bar);

        var left = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 3, 0, 0) };
        // Piles and days are counted on the tabs already.
        left.Controls.AddRange(new Control[] { _chipLogged, _chipUndated, _chipPages, _missing });

        var perPage = new Label { Text = "Pali na stronę", AutoSize = true, Margin = new Padding(0, 8, 0, 0), Tag = "muted" };
        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        right.Controls.AddRange(new Control[] { perPage, _perPage, _format, _btnOpen, _btnGenerateSelected, _btnGenerate });

        bar.Controls.Add(left);
        bar.Controls.Add(right);
        return bar;
    }

    private TableLayoutPanel Fields(Panel host, int labelWidth, int rows)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = rows, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < rows; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowCount = rows + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // takes the slack, so the last field keeps its height
        host.Controls.Add(layout);
        _surfaces.Add(layout);
        return layout;
    }

    private static void AddField(TableLayoutPanel layout, int row, string label, Control field)
        => AddField(layout, row, new Label { Text = label }, field);

    private static void AddField(TableLayoutPanel layout, int row, Label label, Control field)
    {
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.Tag ??= "field";
        if (field is Segmented) field.Anchor = AnchorStyles.Left;
        else field.Dock = DockStyle.Fill;
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(field, 1, row);
    }

    // ---------------------------------------------------------------- theme

    private void ApplyTheme(Palette p)
    {
        _palette = p;
        SuspendLayout();

        BackColor = p.Window;
        ForeColor = p.Text;
        foreach (var bar in _bars) bar.BackColor = p.Bar;
        foreach (var surface in _surfaces) surface.BackColor = p.Card;
        foreach (var surface in _windowSurfaces) surface.BackColor = p.Window;
        Controls[0].BackColor = p.Window;

        _logo.BackColor = p.Accent;
        _logo.ForeColor = p.OnAccent;
        _fileIcon.BackColor = p.IsDark ? Color.FromArgb(0x1f, 0x3a, 0x2a) : Color.FromArgb(0xe7, 0xf3, 0xea);
        _fileIcon.ForeColor = p.OkText;
        _missing.ActiveLinkColor = _missing.VisitedLinkColor = p.Danger;
        _missing.LinkColor = _missingText.StartsWith("Pale bez") ? p.Danger : p.OkText;

        Walk(this, p);
        foreach (var grid in new[] { _gridJournal, _gridPiles, _gridRanges }) StyleGrid(grid, p);
        StyleEditableColumns();
        ShowConcreteFields();

        _moreMenu.Renderer = new ThemedMenuRenderer(p);
        _site.Menu.Renderer = new ThemedMenuRenderer(p);
        _moreMenu.ForeColor = _site.Menu.ForeColor = p.Text;

        ResumeLayout(true);
        NativeTheme.TitleBar(this, p.IsDark);
        Invalidate(true);
    }

    private static void Walk(Control parent, Palette p)
    {
        foreach (Control c in parent.Controls)
        {
            switch (c)
            {
                case IThemed themed:
                    themed.ApplyTheme(p);
                    break;
                case LinkLabel:
                    c.BackColor = c.Parent?.BackColor ?? p.Bar;
                    break;
                case Label label when label.Tag as string == "icon":
                    break;                                // coloured badges, set by ApplyTheme
                case Label label:
                    label.ForeColor = label.Tag as string is "muted" or "field" ? p.Muted : p.Text;
                    label.BackColor = Color.Transparent;
                    break;
                case FlowLayoutPanel flow:
                    flow.BackColor = flow.Parent?.BackColor ?? p.Card;
                    break;
            }
            if (c is not IThemed || c is Card || c is InputBox) Walk(c, p);
        }
    }

    private static void StyleGrid(DataGridView grid, Palette p)
    {
        grid.BackgroundColor = p.Card;
        grid.GridColor = p.GridLine;
        grid.DefaultCellStyle.BackColor = p.Card;
        grid.DefaultCellStyle.ForeColor = p.Text;
        grid.DefaultCellStyle.SelectionBackColor = p.Selected;
        grid.DefaultCellStyle.SelectionForeColor = p.Text;
        grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        grid.ColumnHeadersDefaultCellStyle.BackColor = p.Card;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = p.Muted;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = p.Card;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font(grid.Font, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        // Only the scrollbars: themed as a whole, the grid draws stray cell borders in the dark look.
        foreach (Control child in grid.Controls) NativeTheme.Scrollbars(child, p.IsDark);
    }

    /// <summary>The cells that can be typed in are tinted, so they are found without reading the headers.</summary>
    private void StyleEditableColumns()
    {
        foreach (var grid in new[] { _gridJournal, _gridPiles })
            foreach (DataGridViewColumn column in grid.Columns)
                column.DefaultCellStyle.BackColor = column.ReadOnly ? _palette.Card : _palette.EditCell;
    }

    /// <summary>
    /// Cells drawn entirely by the app - background edge to edge, text, and one
    /// hairline under each row. The grid's own painting left a 1-pixel strip on
    /// the left and top of each cell that showed as light lines in the dark look.
    /// </summary>
    private void PaintCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.Handled || e.RowIndex < 0 || e.Graphics is null || e.CellStyle is null) return;

        FillCell(e);
        var style = e.CellStyle;
        var selected = (e.State & DataGridViewElementStates.Selected) != 0;
        var bounds = new Rectangle(e.CellBounds.X + style.Padding.Left, e.CellBounds.Y,
            e.CellBounds.Width - style.Padding.Horizontal, e.CellBounds.Height);
        var align = style.Alignment is DataGridViewContentAlignment.MiddleRight ? TextFormatFlags.Right : TextFormatFlags.Left;
        TextRenderer.DrawText(e.Graphics, e.FormattedValue?.ToString() ?? "", style.Font ?? Font, bounds,
            selected ? style.SelectionForeColor : style.ForeColor,
            align | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        RowLine(e);
        e.Handled = true;
    }

    /// <summary>
    /// The whole cell, edge to edge, in its background (or selection) colour.
    /// The grid's own background painting leaves the 1-pixel border strip
    /// untouched, which showed as light lines once the borders were left out.
    /// </summary>
    private static void FillCell(DataGridViewCellPaintingEventArgs e)
    {
        var style = e.CellStyle!;
        var selected = (e.State & DataGridViewElementStates.Selected) != 0;
        using var brush = new SolidBrush(selected ? style.SelectionBackColor : style.BackColor);
        var r = e.CellBounds;
        // The grid leaves a 1-pixel strip on the left and on top of each cell
        // (where its borders would go); the fill takes it in.
        e.Graphics!.FillRectangle(brush, r.X - 1, r.Y - 1, r.Width + 1, r.Height + 1);
    }

    private void RowLine(DataGridViewCellPaintingEventArgs e)
    {
        using var pen = new Pen(_palette.GridLine);
        e.Graphics!.DrawLine(pen, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
    }

    /// <summary>The "Metryki" column as a coloured badge: generated, partly, or not.</summary>
    private void PaintMetrykiBadge(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Graphics is null) return;
        if (_gridJournal.Columns[e.ColumnIndex].DataPropertyName != nameof(JournalEntry.Metryki)) return;

        FillCell(e);
        var text = e.FormattedValue as string ?? "";
        var (back, fore, label) = text switch
        {
            "nie" => (_palette.NoBack, _palette.NoText, "nie"),
            "częściowo" => (_palette.WarnBack, _palette.WarnText, "częściowo"),
            "" => (_palette.Card, _palette.Muted, ""),
            _ => (_palette.OkBack, _palette.OkText, "✓ " + text)
        };
        if (label.Length > 0)
        {
            using var font = new Font(_gridJournal.Font.FontFamily, 8f, FontStyle.Bold);
            var size = TextRenderer.MeasureText(label, font);
            var r = new Rectangle(e.CellBounds.X + 8, e.CellBounds.Y + (e.CellBounds.Height - 22) / 2, size.Width + 12, 22);
            Draw.RoundedBox(e.Graphics, r, 11, back, null);
            TextRenderer.DrawText(e.Graphics, label, font, r, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        RowLine(e);
        e.Handled = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeTheme.TitleBar(this, _palette.IsDark);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        foreach (var grid in new[] { _gridJournal, _gridPiles, _gridRanges }) StyleGrid(grid, _palette);
    }

    // --------------------------------------------------------------- helpers

    private static void Column(DataGridView grid, string name, Action<DataGridViewColumn> set)
    {
        if (grid.Columns[name] is { } column) set(column);
    }

    private static void AlignRight(DataGridViewColumn c)
    {
        c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
    }

    private static void SetHeaders(DataGridView grid, Dictionary<string, string> headers)
    {
        foreach (DataGridViewColumn column in grid.Columns)
            if (headers.TryGetValue(column.DataPropertyName, out var text))
                column.HeaderText = text.ToUpper(Polish);
    }

    /// <summary>The top and bottom bars, with a hairline towards the content.</summary>
    private sealed class BarPanel : Panel
    {
        private readonly bool _lineOnTop;
        public BarPanel(bool top) { _lineOnTop = top; DoubleBuffered = true; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var line = ((MainForm?)FindForm())?._palette.Border ?? Color.Gainsboro;
            using var pen = new Pen(line);
            var y = _lineOnTop ? 0 : Height - 1;
            e.Graphics.DrawLine(pen, 0, y, Width, y);
        }
    }
}
