using System.Runtime.InteropServices;

namespace MetrykiPali.Views;

/// <summary>
/// The colours of one look of the window. <see cref="Light"/> and <see cref="Dark"/>
/// are the two the user switches between ("Jasny" / "Ciemny").
/// </summary>
internal sealed record Palette(
    Color Window, Color Bar, Color Card, Color Border, Color Text, Color Muted,
    Color Accent, Color OnAccent, Color Input, Color InputBorder, Color Selected,
    Color EditCell, Color Chip, Color Track, Color GridLine, Color Danger,
    Color OkBack, Color OkText, Color WarnBack, Color WarnText, Color NoBack, Color NoText,
    bool IsDark)
{
    public static readonly Palette Light = new(
        Window: C("#f3f5f8"), Bar: C("#ffffff"), Card: C("#ffffff"), Border: C("#e3e7ed"),
        Text: C("#1f2733"), Muted: C("#7a8594"), Accent: C("#1f6feb"), OnAccent: C("#ffffff"),
        Input: C("#ffffff"), InputBorder: C("#d6dce4"), Selected: C("#eaf2ff"), EditCell: C("#fffbea"),
        Chip: C("#f0f2f6"), Track: C("#e8ecf1"), GridLine: C("#eef1f5"), Danger: C("#c62828"),
        OkBack: C("#e3f5e8"), OkText: C("#1a7f37"), WarnBack: C("#fff3d6"), WarnText: C("#9a6700"),
        NoBack: C("#fde8e8"), NoText: C("#c62828"), IsDark: false);

    public static readonly Palette Dark = new(
        Window: C("#1b1d21"), Bar: C("#1f2226"), Card: C("#23262b"), Border: C("#32363d"),
        Text: C("#e6e8eb"), Muted: C("#8b929c"), Accent: C("#2ec4b6"), OnAccent: C("#0f1113"),
        Input: C("#1b1d21"), InputBorder: C("#3a3f47"), Selected: C("#1d3533"), EditCell: C("#3a3420"),
        Chip: C("#2c3036"), Track: C("#33373e"), GridLine: C("#363a41"), Danger: C("#ff7b72"),
        OkBack: C("#173a24"), OkText: C("#56d364"), WarnBack: C("#3d3113"), WarnText: C("#e3b341"),
        NoBack: C("#3f1c1c"), NoText: C("#ff7b72"), IsDark: true);

    private static Color C(string hex) => ColorTranslator.FromHtml(hex);
}

/// <summary>A control that repaints itself in the colours of a <see cref="Palette"/>.</summary>
internal interface IThemed
{
    void ApplyTheme(Palette palette);
}

/// <summary>Windows calls the window frame and scrollbars can be asked to use dark colours.</summary>
internal static class NativeTheme
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

    /// <summary>A dark title bar (Windows 10 20H1 and later); ignored elsewhere.</summary>
    public static void TitleBar(Form form, bool dark)
    {
        if (!form.IsHandleCreated) return;
        var on = dark ? 1 : 0;
        try
        {
            if (DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(form.Handle, 19, ref on, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    /// <summary>Dark scrollbars on a control that has its own (grids, text boxes).</summary>
    public static void Scrollbars(Control control, bool dark)
    {
        if (!control.IsHandleCreated) return;
        try { SetWindowTheme(control.Handle, dark ? "DarkMode_Explorer" : "Explorer", null); }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
}
