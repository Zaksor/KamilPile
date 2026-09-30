using MetrykiPali.Model;

namespace MetrykiPali.Services;

/// <summary>Reads the source table ("tabelka z palami") from a file.</summary>
public interface IScheduleReader
{
    IReadOnlyList<PileRange> Read(string path);
}

/// <summary>Writes the paginated "METRYKA PALI" workbook.</summary>
public interface IMetrykaWriter
{
    void Write(string path, IReadOnlyList<WorkDay> days, MetrykaSettings settings);
}

/// <summary>
/// Stores the working state between runs. The repository is what lets the
/// journal outlive the process - everything else can be rebuilt from the inputs.
/// </summary>
public interface IProjectRepository
{
    /// <summary>
    /// Where the single project lived before there were several sites. Read
    /// once, to turn it into the first site; never written to again.
    /// </summary>
    string DefaultPath { get; }

    /// <summary>The sites (budowy) kept by the app, by name, in alphabetical order.</summary>
    IReadOnlyList<string> ListSites();

    /// <summary>The file a site with this name is kept in.</summary>
    string SitePath(string name);

    /// <summary>Renames a site's file. Throws IOException if the name is taken.</summary>
    void RenameSite(string name, string newName);

    /// <summary>The site that was open when the app was last closed.</summary>
    string? LastSite { get; set; }

    /// <summary>The folder holding everything; opened for the user on request.</summary>
    string DataDirectory { get; }

    ProjectState? Load(string path);
    void Save(string path, ProjectState state);

    /// <summary>Keeps one dated copy per day, so a bad edit stays recoverable.</summary>
    void BackupOnce(string path);
}
