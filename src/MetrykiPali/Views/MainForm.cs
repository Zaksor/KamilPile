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
    public event EventHandler? NewProjectRequested;
    public event EventHandler? OpenProjectRequested;
    public event EventHandler? SaveProjectAsRequested;
    public event EventHandler? ViewClosing;

    private void WireEvents()
    {
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

        foreach (var box in new[] { _txtBudowa, _txtWykonawca, _txtMetoda })
            box.Leave += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);

        _gridPiles.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            PileEdited?.Invoke(this, new PileEdited(e.RowIndex, _gridPiles.Columns[e.ColumnIndex].DataPropertyName));
        };

        _gridJournal.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (_gridJournal.Columns[e.ColumnIndex].DataPropertyName != nameof(JournalEntry.Wsp)) return;
            if (_gridJournal.Rows[e.RowIndex].DataBoundItem is not JournalEntry entry) return;

            // The presenter answers by refilling this grid, which cannot happen
            // while the grid is still inside its own edit - so hand it over after.
            var edit = new DayFactorEdited(entry.Data, entry.Wsp);
            BeginInvoke(() => DayFactorEdited?.Invoke(this, edit));
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
            [nameof(JournalEntry.Strony)] = "Stron"
        });

        if (_gridJournal.Columns[nameof(JournalEntry.Data)] is { } date)
        {
            date.DefaultCellStyle.Format = "dd.MM.yyyy";
            date.FillWeight = 40;
        }
        if (_gridJournal.Columns[nameof(JournalEntry.Pale)] is { } pale) pale.FillWeight = 200;

        // Only the day's coefficient can be edited here; everything else is derived.
        _gridJournal.ReadOnly = false;
        foreach (DataGridViewColumn column in _gridJournal.Columns)
            column.ReadOnly = column.DataPropertyName != nameof(JournalEntry.Wsp);
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

    public string? AskWhereToSaveProject()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Zapisz projekt jako",
            Filter = $"Projekt metryk (*{JsonProjectRepository.FileExtension})|*{JsonProjectRepository.FileExtension}",
            FileName = "projekt" + JsonProjectRepository.FileExtension
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    public void ShowError(string title, string message)
        => MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    public void ShowInfo(string title, string message)
        => MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);

    public bool Confirm(string title, string question)
        => MessageBox.Show(this, question, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    public void OpenExternally(string path)
    {
        if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
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
        var file = new ToolStripMenuItem("&Projekt");
        file.DropDownItems.Add("&Nowy", null, (_, _) => NewProjectRequested?.Invoke(this, EventArgs.Empty));
        file.DropDownItems.Add("&Otwórz...", null, (_, _) => OpenProjectRequested?.Invoke(this, EventArgs.Empty));
        file.DropDownItems.Add("Zapisz &jako...", null, (_, _) => SaveProjectAsRequested?.Invoke(this, EventArgs.Empty));

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
        var box = new GroupBox { Text = "1. Plik wejściowy", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, AutoSize = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        layout.Controls.Add(_txtSource, 0, 0);
        layout.Controls.Add(_btnLoad, 1, 0);
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
        layout.Controls.Add(hint, 3, 1);
        layout.SetColumnSpan(hint, 2);
        layout.Controls.Add(_btnRemoveDay, 5, 1);

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
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        layout.Controls.Add(_lblStatus, 0, 0);
        layout.Controls.Add(new Label { Text = "Format:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 1, 0);
        layout.Controls.Add(_cmbFormat, 2, 0);
        layout.Controls.Add(_btnGenerate, 3, 0);
        layout.Controls.Add(_btnGenerateSelected, 4, 0);
        layout.Controls.Add(_btnOpen, 5, 0);
        return layout;
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
