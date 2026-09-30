using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace MetrykiPali.Views;

// These controls are built in code, never in the WinForms designer, so none of
// their properties needs designer serialization attributes.
#pragma warning disable WFO1000

// The window's own controls. The standard WinForms ones draw parts of
// themselves in system colours (the arrows of a number box, a drop-down list, a
// tab strip, a date picker), which cannot be made dark; these few are drawn
// entirely by the app, so both looks can be applied to every part.

internal static class Draw
{
    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void RoundedBox(Graphics g, Rectangle r, int radius, Color fill, Color? border)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(r.X + .5f, r.Y + .5f, r.Width - 1, r.Height - 1);
        using var path = Rounded(rect, radius);
        using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
        if (border is { } b) using (var pen = new Pen(b)) g.DrawPath(pen, path);
    }

    public static Font Bold(Font f) => new(f, FontStyle.Bold);
}

/// <summary>A white (or dark) rounded panel with a small caption, holding one part of the window.</summary>
internal sealed class Card : Panel, IThemed
{
    private readonly Label _title = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoSize = false };
    private readonly Panel _header = new() { Dock = DockStyle.Top, Height = 32 };
    private Palette _p = Palette.Light;

    public Panel Body { get; } = new() { Dock = DockStyle.Fill };

    public Card(string title, Control? action = null)
    {
        DoubleBuffered = true;
        Padding = new Padding(16, 10, 16, 14);
        Margin = new Padding(0);
        _title.Text = title.ToUpperInvariant();
        _title.Font = new Font(Font.FontFamily, 8.25f, FontStyle.Bold);
        if (action is not null) { action.Dock = DockStyle.Right; _header.Controls.Add(action); }
        _header.Controls.Add(_title);
        Controls.Add(Body);
        Controls.Add(_header);
    }

    public void ApplyTheme(Palette p)
    {
        _p = p;
        BackColor = p.Window;
        _header.BackColor = Body.BackColor = p.Card;
        _title.ForeColor = p.Muted;
        _title.BackColor = p.Card;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(_p.Window);
        Draw.RoundedBox(e.Graphics, ClientRectangle, 10, _p.Card, _p.Border);
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
}

internal enum ButtonKind { Primary, Secondary, Ghost }

/// <summary>A flat button with rounded corners in the look's colours.</summary>
internal sealed class FlatButton : Button, IThemed
{
    private Palette _p = Palette.Light;
    private bool _hover, _down;

    public ButtonKind Kind { get; set; }

    public FlatButton(string text, ButtonKind kind = ButtonKind.Secondary)
    {
        Text = text;
        Kind = kind;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Height = 32;
        Cursor = Cursors.Hand;
        Font = kind == ButtonKind.Primary || kind == ButtonKind.Ghost ? Draw.Bold(Font) : Font;
        AutoSize = false;
        Width = TextRenderer.MeasureText(text, Font).Width + 30;
    }

    public void ApplyTheme(Palette p) { _p = p; Invalidate(); }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? _p.Card);

        Color fill, text; Color? border;
        switch (Kind)
        {
            case ButtonKind.Primary:
                fill = _p.Accent; text = _p.OnAccent; border = null; break;
            case ButtonKind.Ghost:
                fill = Parent?.BackColor ?? _p.Card; text = _p.Accent; border = null; break;
            default:
                fill = _p.Card; text = _p.Text; border = _p.InputBorder; break;
        }

        if (!Enabled)
        {
            text = _p.Muted;
            if (Kind == ButtonKind.Primary) { fill = _p.Track; }
        }
        else if (_down) fill = ControlPaint.Dark(fill, .05f);
        else if (_hover) fill = Kind == ButtonKind.Primary ? ControlPaint.Light(fill, .15f) : Blend(fill, _p.Accent, .08f);

        Draw.RoundedBox(g, ClientRectangle, 6, fill, border);
        if (Focused && ShowFocusCues && Enabled)
        {
            var r = ClientRectangle; r.Inflate(-2, -2);
            using var path = Draw.Rounded(r, 5);
            using var pen = new Pen(Kind == ButtonKind.Primary ? _p.OnAccent : _p.Accent) { DashStyle = DashStyle.Dot };
            g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static Color Blend(Color a, Color b, float t)
        => Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
}

/// <summary>Two or three options side by side, one chosen - "Excel | PDF", "Jasny | Ciemny".</summary>
internal sealed class Segmented : Control, IThemed
{
    private readonly string[] _items;
    private int _selected;
    private Palette _p = Palette.Light;

    public event EventHandler? SelectedIndexChanged;

    public Segmented(params string[] items)
    {
        _items = items;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
        TabStop = true;
        Height = 30;
        Cursor = Cursors.Hand;
        Width = _items.Sum(i => TextRenderer.MeasureText(i, Font).Width + 24) + 2;
    }

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            value = Math.Clamp(value, 0, _items.Length - 1);
            if (value == _selected) return;
            _selected = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ApplyTheme(Palette p) { _p = p; Invalidate(); }

    private Rectangle Segment(int i)
    {
        var widths = _items.Select(t => TextRenderer.MeasureText(t, Font).Width + 24).ToArray();
        var scale = (Width - 2) / (double)widths.Sum();
        var x = 1;
        for (var k = 0; k < i; k++) x += (int)(widths[k] * scale);
        return new Rectangle(x, 1, (int)(widths[i] * scale), Height - 2);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled) return;
        Focus();
        for (var i = 0; i < _items.Length; i++)
            if (Segment(i).Contains(e.Location)) SelectedIndex = i;
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left) SelectedIndex--;
        if (e.KeyCode == Keys.Right) SelectedIndex++;
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? _p.Card);
        Draw.RoundedBox(g, ClientRectangle, 7, _p.Input, Focused ? _p.Accent : _p.InputBorder);

        for (var i = 0; i < _items.Length; i++)
        {
            var r = Segment(i);
            var on = i == _selected;
            if (on)
            {
                var inner = r; inner.Inflate(-2, -2);
                Draw.RoundedBox(g, inner, 5, Enabled ? _p.Accent : _p.Track, null);
            }
            var color = !Enabled ? _p.Muted : on ? _p.OnAccent : _p.Text;
            using var font = on ? Draw.Bold(Font) : new Font(Font, FontStyle.Regular);
            TextRenderer.DrawText(g, _items[i], font, r, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}

/// <summary>A caption, a figure on the right and a thin bar under them.</summary>
internal sealed class ProgressLine : Control, IThemed
{
    private Palette _p = Palette.Light;
    private string _caption = "", _value = "";
    private double _fraction;

    public ProgressLine()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 40;
    }

    public void Set(string caption, string value, double fraction)
    {
        _caption = caption; _value = value; _fraction = Math.Clamp(double.IsFinite(fraction) ? fraction : 0, 0, 1);
        Invalidate();
    }

    public void ApplyTheme(Palette p) { _p = p; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? _p.Card);
        var top = new Rectangle(0, 2, Width, 20);
        TextRenderer.DrawText(g, _caption, Font, top, _p.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        using (var bold = Draw.Bold(Font))
            TextRenderer.DrawText(g, _value, bold, top, _p.Text, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

        var bar = new Rectangle(0, 26, Width - 1, 8);
        Draw.RoundedBox(g, bar, 4, _p.Track, null);
        if (_fraction > 0)
            Draw.RoundedBox(g, new Rectangle(0, 26, Math.Max(8, (int)((Width - 1) * _fraction)), 8), 4, _p.Accent, null);
    }
}

/// <summary>A small rounded label with a caption and a figure - the counts under the journal.</summary>
internal sealed class Chip : Control, IThemed
{
    private Palette _p = Palette.Light;
    private string _caption, _value = "";

    public Chip(string caption)
    {
        _caption = caption;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Height = 26;
        Margin = new Padding(0, 0, 6, 0);
        Fit();
    }

    public string Value { get => _value; set { _value = value; Fit(); Invalidate(); } }

    private void Fit()
    {
        using var bold = Draw.Bold(Font);
        Width = TextRenderer.MeasureText(_caption + " ", Font).Width + TextRenderer.MeasureText(_value, bold).Width + 14;
    }

    public void ApplyTheme(Palette p) { _p = p; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? _p.Bar);
        Draw.RoundedBox(g, ClientRectangle, 12, _p.Chip, null);
        var captionWidth = TextRenderer.MeasureText(g, _caption + " ", Font, Size.Empty, TextFormatFlags.NoPadding).Width;
        TextRenderer.DrawText(g, _caption + " ", Font, new Rectangle(9, 0, captionWidth + 4, Height), _p.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        using var bold = Draw.Bold(Font);
        TextRenderer.DrawText(g, _value, bold, new Rectangle(9 + captionWidth, 0, Width, Height), _p.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

/// <summary>A rounded frame around a borderless text box, so the field matches the look.</summary>
internal class InputBox : Panel, IThemed
{
    protected Palette P = Palette.Light;

    public TextBox Box { get; } = new() { BorderStyle = BorderStyle.None };

    public InputBox(string placeholder = "")
    {
        DoubleBuffered = true;
        Height = 32;
        Padding = new Padding(9, 8, 9, 6);
        Margin = new Padding(3);
        Box.Dock = DockStyle.Fill;
        Box.PlaceholderText = placeholder;
        Controls.Add(Box);
        Box.GotFocus += (_, _) => Invalidate();
        Box.LostFocus += (_, _) => Invalidate();
        Click += (_, _) => Box.Focus();
    }

    public virtual void ApplyTheme(Palette p)
    {
        P = p;
        BackColor = p.Input;
        Box.BackColor = Enabled ? p.Input : p.Chip;
        Box.ForeColor = p.Text;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Box.BackColor = Enabled ? P.Input : P.Chip;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? P.Card);
        Draw.RoundedBox(e.Graphics, ClientRectangle, 6, Enabled ? P.Input : P.Chip, Box.Focused ? P.Accent : P.InputBorder);
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
}

/// <summary>
/// A number field without spin arrows: typed, or nudged with the arrow keys and
/// the mouse wheel. The value is taken when the field is left or Enter pressed.
/// </summary>
internal sealed class NumberBox : InputBox
{
    private decimal? _value;
    private static readonly CultureInfo Polish = CultureInfo.GetCultureInfo("pl-PL");

    public decimal Minimum { get; init; }
    public decimal Maximum { get; init; } = 1000;
    public decimal Increment { get; init; } = 1;
    public int Decimals { get; init; }

    /// <summary>Empty is allowed and means "no value".</summary>
    public bool AllowEmpty { get; init; }

    public event EventHandler? ValueChanged;

    public NumberBox(string placeholder = "") : base(placeholder)
    {
        Box.TextAlign = HorizontalAlignment.Left;
        Box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { Commit(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Up) { Nudge(1); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Down) { Nudge(-1); e.SuppressKeyPress = true; }
        };
        Box.Leave += (_, _) => Commit();
        Box.MouseWheel += (_, e) => Nudge(Math.Sign(e.Delta));
    }

    public decimal? Value
    {
        get => _value;
        set
        {
            _value = value is { } v ? Math.Clamp(Math.Round(v, Decimals), Minimum, Maximum) : AllowEmpty ? null : Minimum;
            Box.Text = Show(_value);
        }
    }

    private string Show(decimal? v) => v is { } x ? x.ToString("N" + Decimals, Polish).Replace(" ", "") : "";

    private void Nudge(int direction)
    {
        if (!Enabled) return;
        Commit();
        Set((_value ?? Minimum) + direction * Increment);
    }

    private void Commit()
    {
        var text = Box.Text.Trim().Replace(" ", "").Replace('.', ',');
        if (text.Length == 0 && AllowEmpty) { Set(null); return; }
        if (decimal.TryParse(text, NumberStyles.Number, Polish, out var parsed)) Set(parsed);
        else Box.Text = Show(_value);            // not a number: put the last value back
    }

    private void Set(decimal? v)
    {
        var before = _value;
        Value = v;
        if (before != _value) ValueChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>A date field: typed as dd.mm.rrrr, nudged with the arrow keys, or picked from a calendar.</summary>
internal sealed class DateBox : InputBox
{
    private DateTime _value = DateTime.Today;
    private readonly Label _button = new() { Text = "📅", Dock = DockStyle.Right, Width = 24, TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand };

    public DateBox() : base()
    {
        Controls.Add(_button);
        _button.BringToFront();
        Box.BringToFront();
        Box.Text = _value.ToString("dd.MM.yyyy");
        Box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Up) { Value = Parse().AddDays(1); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Down) { Value = Parse().AddDays(-1); e.SuppressKeyPress = true; }
        };
        Box.Leave += (_, _) => Value = Parse();
        _button.Click += (_, _) => ShowCalendar();
    }

    public DateTime Value
    {
        get => Parse();
        set { _value = value.Date; Box.Text = _value.ToString("dd.MM.yyyy"); }
    }

    private DateTime Parse()
        => DateTime.TryParseExact(Box.Text.Trim(), new[] { "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "d.M.yy" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.Date : _value;

    private void ShowCalendar()
    {
        var calendar = new MonthCalendar { MaxSelectionCount = 1, SelectionStart = Parse(), ShowToday = true };
        var host = new ToolStripControlHost(calendar) { Padding = Padding.Empty, Margin = Padding.Empty };
        var drop = new ToolStripDropDown { Padding = Padding.Empty };
        drop.Items.Add(host);
        calendar.DateSelected += (_, e) => { Value = e.Start; drop.Close(); Box.Focus(); };
        drop.Show(this, new Point(0, Height));
    }

    public override void ApplyTheme(Palette p)
    {
        base.ApplyTheme(p);
        _button.BackColor = p.Input;
        _button.ForeColor = p.Muted;
    }
}

/// <summary>The tabs above the journal: text, the chosen one underlined in the accent colour.</summary>
internal sealed class TabStrip : Control, IThemed
{
    private readonly List<string> _tabs = new();
    private int _selected;
    private Palette _p = Palette.Light;

    public event EventHandler? SelectedIndexChanged;

    public TabStrip(params string[] tabs)
    {
        _tabs.AddRange(tabs);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 34;
        Cursor = Cursors.Hand;
    }

    public int SelectedIndex
    {
        get => _selected;
        set { if (value == _selected) return; _selected = value; Invalidate(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty); }
    }

    public void SetText(int index, string text) { _tabs[index] = text; Invalidate(); }

    public void ApplyTheme(Palette p) { _p = p; Invalidate(); }

    private Rectangle Tab(int i, Graphics? g = null)
    {
        var x = 0;
        for (var k = 0; k <= i; k++)
        {
            using var f = k == _selected ? Draw.Bold(Font) : new Font(Font, FontStyle.Regular);
            var w = TextRenderer.MeasureText(_tabs[k], f).Width + 4;
            if (k == i) return new Rectangle(x, 0, w, Height);
            x += w + 18;
        }
        return Rectangle.Empty;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        for (var i = 0; i < _tabs.Count; i++) if (Tab(i).Contains(e.Location)) SelectedIndex = i;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? _p.Card);
        using (var line = new Pen(_p.Border)) g.DrawLine(line, 0, Height - 1, Width, Height - 1);
        for (var i = 0; i < _tabs.Count; i++)
        {
            var r = Tab(i);
            var on = i == _selected;
            using var f = on ? Draw.Bold(Font) : new Font(Font, FontStyle.Regular);
            TextRenderer.DrawText(g, _tabs[i], f, new Rectangle(r.X, 0, r.Width, Height - 4), on ? _p.Accent : _p.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            if (on) using (var b = new SolidBrush(_p.Accent)) g.FillRectangle(b, r.X, Height - 3, r.Width, 3);
        }
    }
}

/// <summary>
/// The site picker on the top bar: "Budowa  ZOO  ⌄". A click lists the sites,
/// with "➕ Nowa budowa..." at the end.
/// </summary>
internal sealed class SitePicker : Control, IThemed
{
    private Palette _p = Palette.Light;
    private IReadOnlyList<string> _sites = Array.Empty<string>();
    private string _current = "";
    private readonly ContextMenuStrip _menu = new();

    public event EventHandler<string>? SitePicked;
    public event EventHandler? NewSiteRequested;

    public SitePicker()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
        TabStop = true;
        Height = 38;
        Width = 320;
        Cursor = Cursors.Hand;
    }

    public ContextMenuStrip Menu => _menu;

    public void Show(IReadOnlyList<string> sites, string current)
    {
        _sites = sites;
        _current = current;
        AccessibleName = "Budowa: " + current;
        Invalidate();
    }

    public void ApplyTheme(Palette p) { _p = p; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); Open(); }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Down or Keys.Space || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode is Keys.Down or Keys.Space or Keys.Enter) Open(); }

    private void Open()
    {
        _menu.Items.Clear();
        foreach (var site in _sites)
        {
            var item = new ToolStripMenuItem(site) { Checked = site == _current };
            var name = site;
            item.Click += (_, _) => SitePicked?.Invoke(this, name);
            _menu.Items.Add(item);
        }
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("➕  Nowa budowa...", null, (_, _) => NewSiteRequested?.Invoke(this, EventArgs.Empty)));
        _menu.MinimumSize = new Size(Width, 0);
        _menu.Show(this, new Point(0, Height + 2));
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? _p.Bar);
        Draw.RoundedBox(g, ClientRectangle, 8, _p.Window, Focused ? _p.Accent : _p.InputBorder);
        using var small = new Font(Font.FontFamily, 8f);
        var labelWidth = TextRenderer.MeasureText("Budowa", small).Width;
        TextRenderer.DrawText(g, "Budowa", small, new Rectangle(12, 0, labelWidth + 4, Height), _p.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        using var big = new Font(Font.FontFamily, 11f, FontStyle.Bold);
        TextRenderer.DrawText(g, _current, big, new Rectangle(18 + labelWidth, 0, Width - labelWidth - 50, Height), _p.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, "⌄", Font, new Rectangle(Width - 30, -3, 20, Height), _p.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Menus (site list, "Więcej") in the look's colours.</summary>
internal sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
{
    private readonly Palette _p;

    public ThemedMenuRenderer(Palette p) : base(new Colours(p)) { _p = p; RoundedEdges = false; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? _p.Text : _p.Muted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        TextRenderer.DrawText(e.Graphics, "✓", e.Item.Font, r, _p.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private sealed class Colours : ProfessionalColorTable
    {
        private readonly Palette _p;
        public Colours(Palette p) { _p = p; UseSystemColors = false; }
        public override Color ToolStripDropDownBackground => _p.Card;
        public override Color ImageMarginGradientBegin => _p.Card;
        public override Color ImageMarginGradientMiddle => _p.Card;
        public override Color ImageMarginGradientEnd => _p.Card;
        public override Color MenuBorder => _p.Border;
        public override Color MenuItemBorder => _p.Selected;
        public override Color MenuItemSelected => _p.Selected;
        public override Color SeparatorDark => _p.Border;
        public override Color SeparatorLight => _p.Card;
        public override Color CheckBackground => _p.Card;
        public override Color CheckSelectedBackground => _p.Selected;
        public override Color CheckPressedBackground => _p.Selected;
    }
}

/// <summary>
/// A grid that never draws the dotted focus frame around the current cell or
/// row, which looks out of place in both looks; the selection colour already
/// shows which day is picked.
/// </summary>
internal sealed class QuietGrid : DataGridView
{
    protected override bool ShowFocusCues => false;
}
