using MetrykiPali.Presentation;
using MetrykiPali.Services;
using MetrykiPali.Views;

namespace MetrykiPali;

/// <summary>
/// The composition root: the one place that decides which real implementations
/// the presenter gets. Swapping any of them - a different reader, a different
/// store - is a change here and nowhere else, and it is exactly what the tests
/// do when they substitute fakes.
/// </summary>
internal static class Program
{
    /// <summary>
    /// A folder with this name next to the .exe holds the journal instead of
    /// %APPDATA%. It is how a test build is handed a copy of the real journal:
    /// whatever the test build does, the journal the site relies on is untouched.
    /// </summary>
    private const string LocalDataFolder = "dane";

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var localData = Path.Combine(AppContext.BaseDirectory, LocalDataFolder);
        var useLocalData = Directory.Exists(localData);

        var view = new MainForm();
        if (useLocalData) view.Text += "   [WERSJA TESTOWA — dane z folderu \"dane\" obok programu]";

        var presenter = new MainPresenter(
            view,
            new PileTableReader(),
            new MetrykaWriter(),
            new JsonProjectRepository(useLocalData ? localData : null));

        presenter.Start();
        Application.Run(view);
    }
}
