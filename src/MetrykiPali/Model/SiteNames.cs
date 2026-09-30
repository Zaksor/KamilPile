namespace MetrykiPali.Model;

/// <summary>
/// Site (budowa) names. A site is kept in a file named after it, so a name has
/// to be a valid file name; this turns what the user typed into one.
/// </summary>
public static class SiteNames
{
    public const int MaxLength = 60;

    /// <summary>The name to show and use for new sites when nothing better is known.</summary>
    public const string Fallback = "Budowa 1";

    /// <summary>
    /// A usable name made from <paramref name="text"/>: characters Windows
    /// forbids in file names become "-", spaces collapse, a trailing dot goes
    /// (Windows drops it silently), and it is cut to <see cref="MaxLength"/>.
    /// Null when nothing usable is left.
    /// </summary>
    public static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var invalid = Path.GetInvalidFileNameChars();
        var chars = text.Select(c => invalid.Contains(c) ? '-' : c).ToArray();
        var name = string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (name.Length > MaxLength) name = name[..MaxLength];
        name = name.Trim().TrimEnd('.', ' ');

        return name.Length == 0 || name.All(c => c == '-') ? null : name;
    }

    /// <summary><paramref name="name"/>, or "name (2)", "name (3)"… - the first not in <paramref name="taken"/>.</summary>
    public static string Unique(string name, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(name)) return name;

        for (var i = 2; ; i++)
        {
            var candidate = $"{name} ({i})";
            if (!used.Contains(candidate)) return candidate;
        }
    }
}
