namespace MetrykiPali.Model;

/// <summary>
/// One row of the source table ("tabelka z palami"): a contiguous range of pile
/// numbers sharing a diameter, design length and reinforcement.
/// </summary>
public sealed class PileRange
{
    public int From { get; set; }
    public int To { get; set; }
    public double Diameter { get; set; }
    public double Length { get; set; }
    public string Reinforcement { get; set; } = "Brak";

    public int Count => To - From + 1;
}

/// <summary>A single pile, i.e. one column of a "METRYKA PALI" sheet.</summary>
public sealed class Pile
{
    public int Number { get; set; }
    public double Diameter { get; set; }
    public double DesignLength { get; set; }
    public double ActualLength { get; set; }
    public double Concrete { get; set; }
    public string ConcretePlant { get; set; } = "";
    public string Reinforcement { get; set; } = "";

    /// <summary>
    /// The day this pile was poured, or null while it is still outstanding.
    /// Set from the work journal; drives the DATA field of the metryka page.
    /// </summary>
    public DateTime? Executed { get; set; }

    /// <summary>
    /// The overbreak coefficient this pile's concrete was worked out with, fixed
    /// when the pile is logged to a day - every day can have its own, and
    /// changing the coefficient field later must not rewrite a day already on
    /// paper. Null while the pile is outstanding: it then follows the field.
    /// </summary>
    public double? ConcreteFactor { get; set; }

    /// <summary>
    /// When this pile's metryka was last written, or null if never - or not
    /// since its day, coefficient or length changed. What "Które pale nie mają
    /// metryk?" goes by.
    /// </summary>
    public DateTime? MetrykaGenerated { get; set; }

    /// <summary>
    /// The concrete used on this pile's whole day, m3, when the day is worked
    /// out from what was delivered rather than from a coefficient; the same on
    /// every pile of the day. The day's total is then shared among its piles by
    /// volume (<see cref="PileMath.Distribute"/>). Null: the coefficient applies.
    /// </summary>
    public double? DayConcreteUsed { get; set; }
}

/// <summary>A day of work: every pile poured on one date.</summary>
public sealed class WorkDay
{
    public DateTime Date { get; set; }
    public List<Pile> Piles { get; set; } = new();
}

/// <summary>One row of the journal grid, ready for display.</summary>
public sealed class JournalEntry
{
    public DateTime Data { get; set; }
    public string Pale { get; set; } = "";
    public int Ilosc { get; set; }
    public double Beton { get; set; }

    /// <summary>
    /// The day's concrete coefficient - the one set, or for a day worked out
    /// from the concrete used, what that comes to (used / theoretical volume).
    /// </summary>
    public double Wsp { get; set; }

    /// <summary>The concrete used that day, m3, or null when the coefficient applies.</summary>
    public double? Zuzyto { get; set; }

    public int Strony { get; set; }

    /// <summary>When the day's metryki were written: a date, "nie" or "częściowo".</summary>
    public string Metryki { get; set; } = "";
}

/// <summary>Everything the generator needs besides the pile list itself.</summary>
public sealed class MetrykaSettings
{
    public string Budowa { get; set; } = "Budynek mieszkalny wielorodzinny, Łódź ul. Tuwima.";
    public string Wykonawca { get; set; } = "Greifbau sp. z o.o., ul. Jerozolimska 2/LU2, 30-555 Kraków";
    public string Metoda { get; set; } = "CFA (Wykonanego w technologii betonowania ciągłego)";
    public string Betoniarnia { get; set; } = "Bosta";
    /// <summary>The footer text, bottom left of every page ("Stopka" in the window).</summary>
    public string Firma { get; set; } = "GREIFBAU SP. Z O.O.";
    public string DokumentacjaNaglowek { get; set; } = "DOKUMENTACJA POWYKONAWCZA";

    /// <summary>
    /// A picture in the footer of every page (a logo, a stamp), as PNG, or
    /// null for none. Kept in the site file itself, so the site carries it
    /// to another computer.
    /// </summary>
    public byte[]? FooterImage { get; set; }

    /// <summary>The name of the file the footer picture came from, to show which it is.</summary>
    public string? FooterImageName { get; set; }

    public FooterPosition FooterImagePosition { get; set; } = FooterPosition.Center;

    /// <summary>The journal grid lists the newest day first.</summary>
    public bool JournalNewestFirst { get; set; }

    /// <summary>How a newly logged day's concrete is worked out.</summary>
    public ConcreteMode ConcreteMode { get; set; } = ConcreteMode.Factor;

    /// <summary>Date proposed in the journal entry box; not written to the metryki.</summary>
    public DateTime Data { get; set; } = DateTime.Today;

    /// <summary>
    /// Overbreak coefficient: actual concrete / theoretical cylinder volume.
    /// 1.30 reproduces the volumes in the reference documentation for most pile
    /// lengths (D=0.4 / L=9 m -> 1.47 m3, L=8 m -> 1.31 m3, L=10 m -> 1.63 m3).
    /// </summary>
    public double ConcreteFactor { get; set; } = 1.30;

    public int PilesPerPage { get; set; } = 12;

    /// <summary>The file type the metryki were last generated as; offered again next time.</summary>
    public MetrykaFormat Format { get; set; } = MetrykaFormat.Xlsx;
}

/// <summary>How the concrete of a day's piles is worked out.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ConcreteMode>))]
public enum ConcreteMode
{
    /// <summary>Theoretical volume × the day's coefficient.</summary>
    Factor,

    /// <summary>The concrete used that day, shared among its piles by volume.</summary>
    Measured
}

/// <summary>Where on the footer the picture sits; the page number stays right, the text left.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<FooterPosition>))]
public enum FooterPosition
{
    Left,
    Center,
    Right
}

/// <summary>The file types the metryki can be written as.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<MetrykaFormat>))]
public enum MetrykaFormat
{
    Xlsx,
    Pdf
}

/// <summary>
/// The whole working state of the application, saved between runs so that piles
/// can be logged day by day and the metryki generated once at the end.
/// </summary>
public sealed class ProjectState
{
    /// <summary>
    /// 2: piles carry their own <see cref="Pile.ConcreteFactor"/>. A version 1
    /// file has none; its logged piles are given the project's coefficient on
    /// load, which is the one their stored volumes were computed with.
    /// </summary>
    public int Version { get; set; } = 2;
    public string? SourcePath { get; set; }
    public MetrykaSettings Settings { get; set; } = new();
    public List<PileRange> Ranges { get; set; } = new();

    /// <summary>
    /// The full pile list, including lengths corrected by hand and the pour date
    /// recorded in the journal (<see cref="Pile.Executed"/>).
    /// </summary>
    public List<Pile> Piles { get; set; } = new();
}
