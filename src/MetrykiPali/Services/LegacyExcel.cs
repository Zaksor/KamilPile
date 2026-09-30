using System.Globalization;
using System.Text;

using ExcelDataReader;

namespace MetrykiPali.Services;

/// <summary>
/// Reads the old binary Excel format (.xls, Excel 97-2003), which ClosedXML
/// cannot open and which designers' summary tables still arrive in.
/// </summary>
internal static class LegacyExcel
{
    static LegacyExcel()
    {
        // .xls stores text in Windows code pages (1250 for Polish), which .NET
        // only decodes once this provider is registered.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Every sheet as rows of cell text; numbers written as invariant decimals.</summary>
    public static List<IReadOnlyList<IReadOnlyList<string>>> ReadSheets(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = ExcelReaderFactory.CreateBinaryReader(stream);

            var sheets = new List<IReadOnlyList<IReadOnlyList<string>>>();
            do
            {
                var rows = new List<IReadOnlyList<string>>();
                while (reader.Read())
                    rows.Add(Enumerable.Range(0, reader.FieldCount).Select(i => Text(reader.GetValue(i))).ToList());
                sheets.Add(rows);
            } while (reader.NextResult());

            return sheets;
        }
        catch (Exception ex) when (ex is not (IOException or UnauthorizedAccessException))
        {
            // A damaged or mislabelled file makes ExcelDataReader throw its own
            // exception types, which the application would not catch.
            throw new InvalidDataException(
                "Nie udało się odczytać tego pliku jako skoroszytu Excela (.xls). " +
                "Plik może być uszkodzony lub zapisany w innym formacie.", ex);
        }
    }

    private static string Text(object? value) => value switch
    {
        null => "",
        double d => d.ToString(CultureInfo.InvariantCulture),
        DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => value.ToString()?.Trim() ?? ""
    };
}
