using System.Text.Json;
using System.Text.Json.Serialization;

using MetrykiPali.Model;

namespace MetrykiPali.Services;

/// <summary>
/// Saves and restores the working state, so piles can be logged over several
/// days and across restarts before the metryki are generated in one go.
/// </summary>
public sealed class JsonProjectRepository : IProjectRepository
{
    public const string FileExtension = ".mpali";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new CalendarDateConverter() }
    };

    /// <summary>
    /// Writes dates as a plain calendar date with no timezone offset.
    ///
    /// A pour date means "the twelfth of September" - it is not an instant. Left
    /// to itself the serializer writes a DateTime whose Kind is Local with an
    /// offset ("2022-09-12T00:00:00+02:00"), and reading that back on a machine
    /// in a different timezone shifts it to the eleventh. The date is the whole
    /// point of a metryka, so it is stored plainly and read back as
    /// <see cref="DateTimeKind.Unspecified"/>.
    /// </summary>
    private sealed class CalendarDateConverter : JsonConverter<DateTime>
    {
        private const string Format = "yyyy-MM-ddTHH:mm:ss";

        public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => DateTime.SpecifyKind(reader.GetDateTime(), DateTimeKind.Unspecified);

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString(Format, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Where the journal lives unless a different folder is given.</summary>
    public static string StandardDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MetrykiPali");

    /// <param name="directory">
    /// Folder holding projekt.mpali. Defaults to %APPDATA%\MetrykiPali; a test
    /// copy of the app points it elsewhere so it cannot touch the real journal.
    /// </param>
    public JsonProjectRepository(string? directory = null)
    {
        DataDirectory = directory ?? StandardDirectory;
        DefaultPath = Path.Combine(DataDirectory, "projekt" + FileExtension);
    }

    public string DataDirectory { get; }

    /// <summary>The single project from before there were sites; read once to make the first one.</summary>
    public string DefaultPath { get; }

    // ---------------------------------------------------------------- sites

    /// <summary>One .mpali file per site, named after it.</summary>
    private string SitesDirectory => Path.Combine(DataDirectory, "budowy");

    private string LastSiteFile => Path.Combine(DataDirectory, "ostatnia-budowa.txt");

    private static readonly StringComparer PolishOrder =
        StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("pl-PL"), ignoreCase: true);

    public IReadOnlyList<string> ListSites()
    {
        if (!Directory.Exists(SitesDirectory)) return Array.Empty<string>();

        return Directory.GetFiles(SitesDirectory, "*" + FileExtension)
            .Where(f => Path.GetExtension(f).Equals(FileExtension, StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .OrderBy(n => n, PolishOrder)
            .ToList();
    }

    public string SitePath(string name) => Path.Combine(SitesDirectory, name + FileExtension);

    public void RenameSite(string name, string newName)
    {
        var target = SitePath(newName);
        if (File.Exists(target) && !string.Equals(name, newName, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"Budowa o nazwie \"{newName}\" już istnieje.");

        File.Move(SitePath(name), target);
        if (string.Equals(LastSite, name, StringComparison.OrdinalIgnoreCase)) LastSite = newName;
    }

    public string? LastSite
    {
        get
        {
            try { return File.Exists(LastSiteFile) ? File.ReadAllText(LastSiteFile).Trim() : null; }
            catch (IOException) { return null; }
        }
        set
        {
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(LastSiteFile, value ?? "");
        }
    }

    public void Save(string path, ProjectState state)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        // Write to a temporary file first so an interrupted save cannot destroy
        // a journal that may represent weeks of site records.
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, Options));
        File.Move(temp, path, overwrite: true);
    }

    public ProjectState? Load(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            return JsonSerializer.Deserialize<ProjectState>(File.ReadAllText(path), Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Keeps one dated backup per day, so a bad edit is recoverable.</summary>
    public void BackupOnce(string path)
    {
        if (!File.Exists(path)) return;

        var backup = Path.Combine(
            Path.GetDirectoryName(path)!,
            $"{Path.GetFileNameWithoutExtension(path)}-{DateTime.Today:yyyyMMdd}{FileExtension}.bak");

        if (!File.Exists(backup)) File.Copy(path, backup);
    }
}
