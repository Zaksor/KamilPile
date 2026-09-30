using System.ComponentModel;
using System.Diagnostics;
using MetrykiPali.Model;
using MetrykiPali.Presentation;
using MetrykiPali.Services;

namespace MetrykiPali.Views;

/// <summary>
/// The window. A passive view: it builds the controls, exposes their contents
/// as properties, and raises an event when the user does something. It decides
/// nothing - <see cref="MainPresenter"/> does.
/// </summary>
public sealed class MainForm : Form, IMainView
{
    private BindingList<Pile> _piles = new();
    private readonly BindingList<JournalEntry> _journal = new();

    /// <summary>The last entry of the site list, which adds a site instead of opening one.</summary>
    private const string NewSiteItem = "➕  Nowa budowa...";

    private readonly ComboBox _cmbSite = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Button _btnNewSite = new() { Text = "Nowa budowa...", Dock = DockStyle.Fill };
    private string _currentSite = "";
    private bool _fillingSites;

    private readonly TextBox _txtSource = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly TextBox _txtBudowa = new() { Dock = DockStyle.Fill };
    private readonly TextBox _txtWykonawca = new() { Dock = DockStyle.Fill };
    private readonly TextBox _txtMetoda = new() { Dock = DockStyle.Fill };
    private readonly TextBox _txtBetoniarnia = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _numFactor = new()
    {
        DecimalPlaces = 2, Increment = 0.01m, Minimum = 1.00m, Maximum = 3.00m, Dock = DockStyle.Fill
    };
    private readonly NumericUpDown _numPerPage = new() { Minimum = 1, Maximum = 12, Value = 12, Dock = DockStyle.Fill };

    private readonly DateTimePicker _dtDay = new() { Format = DateTimePickerFormat.Short, Dock = DockStyle.Fill };
    private readonly TextBox _txtDayPiles = new() { Dock = DockStyle.Fill, PlaceholderText = "np. 1-10, 25, 30-33" };
    private readonly Button _btnAddDay = new() { Text = "Dodaj do dziennika", Dock = DockStyle.Fill };
    private readonly Button _btnAddSelected = new() { Text = "Dodaj zaznaczone z listy pali", Dock = DockStyle.Fill };
    private readonly Button _btnRemoveDay = new() { Text = "Usuń zaznaczony dzień", Dock = DockStyle.Fill };

    private readonly DataGridView _gridJournal = NewGrid();
    private readonly DataGridView _gridRanges = NewGrid();
    private readonly DataGridView _gridPiles = NewGrid();

    private readonly Button _btnLoad = new() { Text = "Wczytaj tabelkę...", Dock = DockStyle.Fill, Height = 30 };
    private readonly Button _btnGenerate = new() { Text = "Generuj metryki — wszystkie dni", Dock = DockStyle.Fill, Height = 34, Enabled = false };
    private readonly Button _btnGenerateSelected = new() { Text = "Generuj metryki — zaznaczone dni", Dock = DockStyle.Fill, Height = 34, Enabled = false };
    private readonly Button _btnOpen = new() { Text = "Otwórz wygenerowany plik", Dock = DockStyle.Fill, Height = 34, Enabled = false };
    private readonly ComboBox _cmbFormat = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(3, 8, 3, 3),
        Items = { "Excel (.xlsx)", "PDF (.pdf)" }, SelectedIndex = 0
    };
    private readonly Label _lblStatus =new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly LinkLabel _lnkMissing = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };

    private readonly TextBox _txtFooter = new() { Dock = DockStyle.Fill };
    private readonly Label _lblFooterImage = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly Button _btnFooterImage = new() { Text = "Wybierz obraz...", AutoSize = true };
    private readonly Button _btnFooterClear = new() { Text = "Usuń", AutoSize = true };
    private readonly ComboBox _cmbFooterPosition = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Width = 90,
        Items = { "po lewej", "na środku", "po prawej" }, SelectedIndex = 1
    };

    private readonly ComboBox _cmbConcreteMode = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill,
        Items = { "ze współczynnika", "z ilości zużytej w dniu" }, SelectedIndex = 0
    };
    private readonly NumericUpDown _numUsed = new()
    {
        DecimalPlaces = 2, Increment = 0.5m, Minimum = 0m, Maximum = (decimal)MainPresenter.MaxDayConcrete, Width = 90,
        Margin = new Padding(3, 3, 3, 3)
    };
    private readonly Label _lblUsed = new() { Text = "m³ betonu zużytego tego dnia (0 = wpiszę później)", AutoSize = true, Margin = new Padding(3, 6, 12, 3) };

    private readonly ComboBox _cmbJournalOrder = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill,
        Items = { "od najstarszego", "od najnowszego" }, SelectedIndex = 0
    };

    public MainForm()
    {
        Text = "Metryki pali — generator dokumentacji powykonawczej";
        Width = 1240;
        Height = 880;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 700);

        BuildMenu();
        BuildUi();
        WireEvents();
    }

    // ------------------------------------------------------- IMainView state

    string IMainView.SourcePath { get => _txtSource.Text; set => _txtSource.Text = value; }
    string IMainView.Budowa { get => _txtBudowa.Text; set => _txtBudowa.Text = value; }
    string IMainView.Wykonawca { get => _txtWykonawca.Text; set => _txtWykonawca.Text = value; }
    string IMainView.Metoda { get => _txtMetoda.Text; set => _txtMetoda.Text = value; }
    string IMainView.Betoniarnia { get => _txtBetoniarnia.Text; set => _txtBetoniarnia.Text = value; }
    string IMainView.JournalPiles { get => _txtDayPiles.Text; set => _txtDayPiles.Text = value; }
    string IMainView.StatusText { get => _lblStatus.Text; set => _lblStatus.Text = value; }

    double IMainView.ConcreteFactor
    {
        get => (double)_numFactor.Value;
        set => _numFactor.Value = Math.Clamp((decimal)value, _numFactor.Minimum, _numFactor.Maximum);
    }

    int IMainView.PilesPerPage
    {
        get => (int)_numPerPage.Value;
        set => _numPerPage.Value = Math.Clamp(value, _numPerPage.Minimum, _numPerPage.Maximum);
    }

    // Item order in _cmbFormat follows the enum: 0 = Xlsx, 1 = Pdf.
    MetrykaFormat IMainView.OutputFormat
    {
        get => _cmbFormat.SelectedIndex == 1 ? MetrykaFormat.Pdf : MetrykaFormat.Xlsx;
        set => _cmbFormat.SelectedIndex = value == MetrykaFormat.Pdf ? 1 : 0;
    }

    DateTime IMainView.JournalDate { get => _dtDay.Value.Date; set => _dtDay.Value = value; }

    string IMainView.FooterText { get => _txtFooter.Text; set => _txtFooter.Text = value; }
    string IMainView.FooterImageLabel { get => _lblFooterImage.Text; set { _lblFooterImage.Text = value; _btnFooterClear.Enabled = value != "brak"; } }
    string IMainView.MissingMetrykiText { get => _lnkMissing.Text; set => _lnkMissing.Text = value; }

    // Item order follows the enum: 0 = Left, 1 = Center, 2 = Right.
    FooterPosition IMainView.FooterImagePosition
    {
        get => (FooterPosition)Math.Max(0, _cmbFooterPosition.SelectedIndex);
        set => _cmbFooterPosition.SelectedIndex = (int)value;
    }

    // Item order follows the enum: 0 = Factor, 1 = Measured.
    ConcreteMode IMainView.ConcreteMode
    {
        get => _cmbConcreteMode.SelectedIndex == 1 ? ConcreteMode.Measured : ConcreteMode.Factor;
        set => _cmbConcreteMode.SelectedIndex = value == ConcreteMode.Measured ? 1 : 0;
    }

    double? IMainView.JournalConcreteUsed
    {
        get => _numUsed.Value > 0 ? (double)_numUsed.Value : null;
        set => _numUsed.Value = value is { } v ? Math.Clamp((decimal)v, _numUsed.Minimum, _numUsed.Maximum) : 0;
    }

    bool IMainView.JournalNewestFirst
    {
        get => _cmbJournalOrder.SelectedIndex == 1;
        set => _cmbJournalOrder.SelectedIndex = value ? 1 : 0;
    }

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
    public event EventHandler? MissingMetrykiRequested;
    public event EventHandler? JournalOrderChanged;
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
        _btnNewSite.Click += (_, _) => NewSiteRequested?.Invoke(this, EventArgs.Empty);
        _cmbSite.SelectedIndexChanged += (_, _) =>
        {
            if (_fillingSites || _cmbSite.SelectedItem is not string picked) return;

            if (picked == NewSiteItem)
            {
                // Put the list back on the open site; the presenter refills it
                // if a site is added.
                SelectSite(_currentSite);
                BeginInvoke(() => NewSiteRequested?.Invoke(this, EventArgs.Empty));
                return;
            }

            BeginInvoke(() => SiteSelected?.Invoke(this, picked));
        };

        _btnLoad.Click += (_, _) => LoadScheduleRequested?.Invoke(this, EventArgs.Empty);
        _btnAddDay.Click += (_, _) => AddDayRequested?.Invoke(this, EventArgs.Empty);
        _btnAddSelected.Click += (_, _) => AddSelectedPilesRequested?.Invoke(this, EventArgs.Empty);
        _btnRemoveDay.Click += (_, _) => RemoveDayRequested?.Invoke(this, EventArgs.Empty);
        _btnGenerate.Click += (_, _) => GenerateRequested?.Invoke(this, EventArgs.Empty);
        _btnGenerateSelected.Click += (_, _) => GenerateSelectedDaysRequested?.Invoke(this, EventArgs.Empty);
        _btnOpen.Click += (_, _) => OpenOutputRequested?.Invoke(this, EventArgs.Empty);

        _numFactor.ValueChanged += (_, _) => ConcreteFactorChanged?.Invoke(this, EventArgs.Empty);
        _numPerPage.ValueChanged += (_, _) => PilesPerPageChanged?.Invoke(this, EventArgs.Empty);
        _txtBetoniarnia.TextChanged += (_, _) => ConcretePlantChanged?.Invoke(this, EventArgs.Empty);

        _cmbFormat.SelectedIndexChanged += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
        _cmbFooterPosition.SelectedIndexChanged += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
        _btnFooterImage.Click += (_, _) => FooterImageRequested?.Invoke(this, EventArgs.Empty);
        _btnFooterClear.Click += (_, _) => FooterImageCleared?.Invoke(this, EventArgs.Empty);
        _lnkMissing.LinkClicked += (_, _) => MissingMetrykiRequested?.Invoke(this, EventArgs.Empty);
        _cmbJournalOrder.SelectedIndexChanged += (_, _) => JournalOrderChanged?.Invoke(this, EventArgs.Empty);

        // Clicking the "Data" header flips the order, as users expect of a date column.
        _gridJournal.ColumnHeaderMouseClick += (_, e) =>
        {
            if (_gridJournal.Columns[e.ColumnIndex].DataPropertyName != nameof(JournalEntry.Data)) return;
            _cmbJournalOrder.SelectedIndex = 1 - _cmbJournalOrder.SelectedIndex;
        };

        foreach (var box in new[] { _txtBudowa, _txtWykonawca, _txtMetoda, _txtFooter })
            box.Leave += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);

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

        _cmbConcreteMode.SelectedIndexChanged += (_, _) =>
        {
            ShowConcreteUsedField();
            ConcreteModeChanged?.Invoke(this, EventArgs.Empty);
        };

        _gridJournal.DataError += (_, e) =>
        {
            e.Cancel = true;
            MessageBox.Show(this, "Wpisz liczbę, np. 1,25.", "Błędny współczynnik",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };

        _txtDayPiles.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            AddDayRequested?.Invoke(this, EventArgs.Empty);
        };

        FormClosing += (_, _) => ViewClosing?.Invoke(this, EventArgs.Empty);
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
            [nameof(Pile.Concrete)] = "Beton [m3]",
            [nameof(Pile.ConcretePlant)] = "Betoniarnia",
            [nameof(Pile.Reinforcement)] = "Zbrojenie",
            [nameof(Pile.Executed)] = "Data wykonania",
            [nameof(Pile.ConcreteFactor)] = "Wsp. betonu"
        });

        if (_gridPiles.Columns[nameof(Pile.Executed)] is { } executed)
        {
            executed.DefaultCellStyle.Format = "dd.MM.yyyy";
            executed.ReadOnly = true;
        }

        // Set per day on the journal tab; shown here so each pile's volume can be traced.
        if (_gridPiles.Columns[nameof(Pile.ConcreteFactor)] is { } factor)
        {
            factor.DefaultCellStyle.Format = "0.00";
            factor.ReadOnly = true;
        }
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
            [nameof(JournalEntry.Beton)] = "Beton [m3]",
            [nameof(JournalEntry.Wsp)] = "Wsp. betonu (edytuj)",
            [nameof(JournalEntry.Strony)] = "Stron",
            [nameof(JournalEntry.Metryki)] = "Metryki wygenerowane"
        });

        // The order is chosen with "Kolejność dni"; the grid's own sorting would fight it.
        foreach (DataGridViewColumn column in _gridJournal.Columns)
            column.SortMode = DataGridViewColumnSortMode.Programmatic;
        if (_gridJournal.Columns[nameof(JournalEntry.Data)] is { } dataColumn)
            dataColumn.HeaderCell.SortGlyphDirection = _cmbJournalOrder.SelectedIndex == 1 ? SortOrder.Descending : SortOrder.Ascending;

        if (_gridJournal.Columns[nameof(JournalEntry.Data)] is { } date)
        {
            date.DefaultCellStyle.Format = "dd.MM.yyyy";
            date.FillWeight = 60;
        }
        if (_gridJournal.Columns[nameof(JournalEntry.Pale)] is { } pale) pale.FillWeight = 200;

        // Only the day's coefficient and its concrete used can be edited here;
        // everything else is derived.
        _gridJournal.ReadOnly = false;
        foreach (DataGridViewColumn column in _gridJournal.Columns)
            column.ReadOnly = column.DataPropertyName is not (nameof(JournalEntry.Wsp) or nameof(JournalEntry.Zuzyto));
        if (_gridJournal.Columns[nameof(JournalEntry.Zuzyto)] is { } zuzyto)
        {
            zuzyto.HeaderText = "Zużyto [m3] (edytuj)";
            zuzyto.DefaultCellStyle.Format = "0.00";
            zuzyto.DefaultCellStyle.NullValue = "";
            zuzyto.DefaultCellStyle.DataSourceNullValue = null;
            zuzyto.DefaultCellStyle.BackColor = Color.LightYellow;
            zuzyto.FillWeight = 90;
            zuzyto.ToolTipText = "Ile betonu zużyto tego dnia (np. z WZ). Program rozdzieli go na pale według ich objętości. " +
                                 "Wyczyść komórkę, aby wrócić do liczenia ze współczynnika.";
        }
        if (_gridJournal.Columns[nameof(JournalEntry.Wsp)] is { } wsp)
        {
            wsp.DefaultCellStyle.Format = "0.00";
            wsp.DefaultCellStyle.BackColor = Color.LightYellow;
            wsp.FillWeight = 90;
            wsp.ToolTipText = "Kliknij dwukrotnie i wpisz nowy współczynnik tego dnia, np. 1,25. " +
                              "Beton pali z tego dnia przeliczy się od razu; inne dni się nie zmienią.";
        }
    }

    public void RefreshPiles() => _gridPiles.Refresh();

    public void ShowSites(IReadOnlyList<string> sites, string current)
    {
        _fillingSites = true;
        try
        {
            _cmbSite.Items.Clear();
            foreach (var site in sites) _cmbSite.Items.Add(site);
            _cmbSite.Items.Add(NewSiteItem);
            _currentSite = current;
            SelectSite(current);
        }
        finally
        {
            _fillingSites = false;
        }

        Text = $"Metryki pali — {current}" + (_testMarker ?? "");
    }

    private void SelectSite(string name)
    {
        var was = _fillingSites;
        _fillingSites = true;
        _cmbSite.SelectedItem = _cmbSite.Items.Contains(name) ? name : null;
        _fillingSites = was;
    }

    private string? _testMarker;

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
            Title = "Otwórz projekt",
            Filter = $"Projekt metryk (*{JsonProjectRepository.FileExtension})|*{JsonProjectRepository.FileExtension}"
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
        using var form = new Form
        {
            Text = title, StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false,
            MinimizeBox = false, ClientSize = new Size(720, 460), MinimumSize = new Size(400, 250)
        };
        var box = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = true, Dock = DockStyle.Fill,
            Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n"), Font = new Font("Consolas", 10f), BackColor = SystemColors.Window
        };
        var close = new Button { Text = "Zamknij", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 32 };
        form.Controls.Add(box);
        form.Controls.Add(close);
        form.AcceptButton = close;
        form.CancelButton = close;
        form.Shown += (_, _) => { box.SelectionStart = 0; box.SelectionLength = 0; };
        form.ShowDialog(this);
    }

    public string? AskForText(string title, string prompt, string initial)
    {
        using var form = new Form
        {
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, ClientSize = new Size(460, 130)
        };
        var label = new Label { Text = prompt, Left = 12, Top = 12, Width = 436, Height = 36 };
        var box = new TextBox { Text = initial, Left = 12, Top = 52, Width = 436 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 272, Top = 90, Width = 85 };
        var cancel = new Button { Text = "Anuluj", DialogResult = DialogResult.Cancel, Left = 363, Top = 90, Width = 85 };
        form.Controls.AddRange(new Control[] { label, box, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        return form.ShowDialog(this) == DialogResult.OK ? box.Text : null;
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

    private static DataGridView NewGrid() => new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.CellSelect,
        RowHeadersVisible = false
    };

    private void BuildMenu()
    {
        var file = new ToolStripMenuItem("&Budowa");
        file.DropDownItems.Add("&Nowa budowa...", null, (_, _) => NewSiteRequested?.Invoke(this, EventArgs.Empty));
        file.DropDownItems.Add("&Zmień nazwę budowy...", null, (_, _) => RenameSiteRequested?.Invoke(this, EventArgs.Empty));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("&Dodaj budowę z pliku...", null, (_, _) => OpenProjectRequested?.Invoke(this, EventArgs.Empty));
        file.DropDownItems.Add("Zapisz &kopię budowy jako...", null, (_, _) => SaveProjectAsRequested?.Invoke(this, EventArgs.Empty));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Pokaż &folder z danymi", null, (_, _) => ShowDataFolderRequested?.Invoke(this, EventArgs.Empty));

        var menu = new MenuStrip();
        menu.Items.Add(file);
        MainMenuStrip = menu;
        Controls.Add(menu);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10, 34, 10, 10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildSourceBox(), 0, 0);
        root.Controls.Add(BuildSettingsBox(), 0, 1);
        root.Controls.Add(BuildJournalBox(), 0, 2);
        root.Controls.Add(BuildTabs(), 0, 3);
        root.Controls.Add(BuildActionBar(), 0, 4);

        Controls.Add(root);
        root.BringToFront();
    }

    private Control BuildSourceBox()
    {
        var box = new GroupBox { Text = "1. Budowa i jej tabelka z palami", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, AutoSize = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        layout.Controls.Add(new Label { Text = "Budowa:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        layout.Controls.Add(_cmbSite, 1, 0);
        layout.Controls.Add(_btnNewSite, 2, 0);
        layout.Controls.Add(new Label { Text = "Tabelka z palami:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        layout.Controls.Add(_txtSource, 1, 1);
        layout.Controls.Add(_btnLoad, 2, 1);
        box.Controls.Add(layout);
        return box;
    }

    private Control BuildSettingsBox()
    {
        var box = new GroupBox { Text = "2. Nagłówek metryki i obliczenia", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoSize = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

        AddField(layout, 0, "Budowa:", _txtBudowa, "Betoniarnia:", _txtBetoniarnia);
        AddField(layout, 1, "Wykonawca:", _txtWykonawca, "Wsp. betonu (nowe dni):", _numFactor);
        AddField(layout, 2, "Metoda:", _txtMetoda, "Pali na stronę:", _numPerPage);

        // The footer: its text on one row, its picture on the next, across the box.
        AddField(layout, 3, "Stopka (tekst):", _txtFooter, "Beton liczony:", _cmbConcreteMode);

        _lblFooterImage.AutoSize = false;
        _lblFooterImage.Dock = DockStyle.None;
        _lblFooterImage.Size = new Size(220, 23);
        _lblFooterImage.Margin = new Padding(3, 6, 3, 3);
        var positionLabel = new Label { Text = "położenie:", AutoSize = true, Margin = new Padding(12, 7, 3, 3) };
        // Not AutoSize: a self-sizing panel spanning percentage columns makes the
        // table claim its full width as a minimum and push the window wider.
        var picture = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = false, Size = new Size(100, 30), WrapContents = false, Margin = Padding.Empty };
        picture.Controls.AddRange(new Control[] { _lblFooterImage, _btnFooterImage, _btnFooterClear, positionLabel, _cmbFooterPosition });
        layout.Controls.Add(new Label { Text = "Obraz w stopce:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
        layout.Controls.Add(picture, 1, 4);
        layout.SetColumnSpan(picture, 3);

        box.Controls.Add(layout);
        return box;
    }

    private Control BuildJournalBox()
    {
        var box = new GroupBox { Text = "3. Dziennik robót — wpisz pale wykonane danego dnia", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 2, AutoSize = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));

        layout.Controls.Add(new Label { Text = "Data wykonania:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        layout.Controls.Add(_dtDay, 1, 0);
        layout.Controls.Add(new Label { Text = "Pale:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 2, 0);
        layout.Controls.Add(_txtDayPiles, 3, 0);
        layout.Controls.Add(_btnAddDay, 4, 0);
        layout.Controls.Add(_btnAddSelected, 5, 0);

        var hint = new Label
        {
            Text = "Zakresy i pojedyncze numery, np. \"1-10, 25, 30-33\". Enter zatwierdza. " +
                   "Pal wpisany ponownie z inną datą zostanie przeniesiony.",
            Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText, AutoSize = false
        };
        // Under the pile numbers: the concrete used that day (only when the day's
        // concrete comes from it), then the hint.
        hint.AutoSize = true;
        hint.Dock = DockStyle.None;
        hint.Margin = new Padding(3, 6, 3, 3);
        _hint = hint;
        var under = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = false, Size = new Size(100, 30), WrapContents = false, Margin = Padding.Empty };
        under.Controls.AddRange(new Control[] { _numUsed, _lblUsed, hint });
        layout.Controls.Add(under, 3, 1);
        layout.SetColumnSpan(under, 2);
        ShowConcreteUsedField();
        layout.Controls.Add(_btnRemoveDay, 5, 1);
        layout.Controls.Add(new Label { Text = "Kolejność dni:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        layout.Controls.Add(_cmbJournalOrder, 1, 1);

        box.Controls.Add(layout);
        return box;
    }

    private Control BuildTabs()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };

        // Whole rows, several at once (Ctrl/Shift): the selection is which days
        // "Generuj metryki — zaznaczone dni" writes.
        _gridJournal.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _gridJournal.MultiSelect = true;

        var tabJournal = new TabPage("Dziennik (dni)");
        tabJournal.Controls.Add(_gridJournal);
        var tabPiles = new TabPage("Pale");
        tabPiles.Controls.Add(_gridPiles);
        var tabRanges = new TabPage("Zakresy z tabelki");
        tabRanges.Controls.Add(_gridRanges);

        tabs.TabPages.Add(tabJournal);
        tabs.TabPages.Add(tabPiles);
        tabs.TabPages.Add(tabRanges);
        return tabs;
    }

    private Control BuildActionBar()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 2, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));

        // Bottom left: the counts, and under them the way to see which piles
        // still lack a metryka. The buttons take both rows.
        layout.Controls.Add(_lblStatus, 0, 0);
        layout.Controls.Add(_lnkMissing, 0, 1);

        var format = new Label { Text = "Format:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };
        foreach (var (control, column) in new (Control, int)[] { (format, 1), (_cmbFormat, 2), (_btnGenerate, 3), (_btnGenerateSelected, 4), (_btnOpen, 5) })
        {
            layout.Controls.Add(control, column, 0);
            layout.SetRowSpan(control, 2);
        }
        return layout;
    }

    private Label? _hint;

    private void ShowConcreteUsedField()
    {
        var measured = _cmbConcreteMode.SelectedIndex == 1;
        _numUsed.Visible = _lblUsed.Visible = measured;
        if (_hint is not null)
            _hint.Text = measured
                ? "Enter zatwierdza."
                : "Zakresy i pojedyncze numery, np. \"1-10, 25, 30-33\". Enter zatwierdza. " +
                  "Pal wpisany ponownie z inną datą zostanie przeniesiony.";
    }

    private static void AddField(TableLayoutPanel layout, int row, string leftLabel, Control left, string rightLabel, Control right)
    {
        layout.Controls.Add(new Label { Text = leftLabel, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        layout.Controls.Add(left, 1, row);
        layout.Controls.Add(new Label { Text = rightLabel, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 2, row);
        layout.Controls.Add(right, 3, row);
    }

    private static void SetHeaders(DataGridView grid, Dictionary<string, string> headers)
    {
        foreach (DataGridViewColumn column in grid.Columns)
            if (headers.TryGetValue(column.DataPropertyName, out var text))
                column.HeaderText = text;
    }
}
