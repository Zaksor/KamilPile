using MetrykiPali.Model;

namespace MetrykiPali.Services;

/// <summary>
/// Writes the metryki in the format the file name asks for: .pdf goes to the
/// PDF writer, anything else to the workbook. The presenter only chooses the
/// name, so it stays unaware of how either format is produced.
/// </summary>
public sealed class MetrykaFileWriter : IMetrykaWriter
{
    private readonly IMetrykaWriter _xlsx;
    private readonly IMetrykaWriter _pdf;

    public MetrykaFileWriter(IMetrykaWriter xlsx, IMetrykaWriter pdf)
    {
        _xlsx = xlsx;
        _pdf = pdf;
    }

    public void Write(string path, IReadOnlyList<WorkDay> days, MetrykaSettings settings)
        => (IsPdf(path) ? _pdf : _xlsx).Write(path, days, settings);

    public static bool IsPdf(string path)
        => string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);
}
