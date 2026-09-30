namespace MetrykiPali.Tests;

/// <summary>
/// The journal is the one thing in this app that cannot be recreated from the
/// inputs - it is weeks of site records - so its persistence is covered closely.
/// </summary>
public sealed class ProjectStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mpali-tests-" + Guid.NewGuid().ToString("N"));

    private string Path_(string name) => Path.Combine(_dir, name);

    public ProjectStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static ProjectState SampleProject() => new()
    {
        SourcePath = @"C:\budowa\tabelka z palami.xlsx",
        Settings = new MetrykaSettings
        {
            Budowa = "Budynek mieszkalny wielorodzinny, Łódź ul. Tuwima.",
            Wykonawca = "Greifbau sp. z o.o., Kraków",
            ConcreteFactor = 1.25,
            PilesPerPage = 12
        },
        Ranges =
        {
            new PileRange { From = 1, To = 10, Diameter = 0.4, Length = 7, Reinforcement = "Brak" }
        },
        Piles =
        {
            new Pile { Number = 1, Diameter = 0.4, DesignLength = 7, ActualLength = 7.4, Concrete = 1.14, Executed = new DateTime(2022, 9, 12) },
            new Pile { Number = 2, Diameter = 0.4, DesignLength = 7, ActualLength = 7, Concrete = 1.14 }
        }
    };

    [Fact]
    public void Saves_and_restores_the_whole_project()
    {
        var path = Path_("projekt.mpali");
        Repository.Save(path, SampleProject());

        var loaded = Repository.Load(path);

        Assert.NotNull(loaded);
        Assert.Equal(@"C:\budowa\tabelka z palami.xlsx", loaded!.SourcePath);
        Assert.Single(loaded.Ranges);
        Assert.Equal(2, loaded.Piles.Count);
        Assert.Equal(1.25, loaded.Settings.ConcreteFactor);
    }

    [Fact]
    public void Keeps_the_pour_dates()
    {
        var path = Path_("projekt.mpali");
        Repository.Save(path, SampleProject());

        var loaded = Repository.Load(path)!;

        Assert.Equal(new DateTime(2022, 9, 12), loaded.Piles[0].Executed);
        Assert.Null(loaded.Piles[1].Executed);
    }

    [Fact]
    public void Keeps_hand_corrected_lengths()
    {
        var path = Path_("projekt.mpali");
        Repository.Save(path, SampleProject());

        Assert.Equal(7.4, Repository.Load(path)!.Piles[0].ActualLength);
    }

    [Fact]
    public void Keeps_polish_characters_intact()
    {
        var path = Path_("projekt.mpali");
        Repository.Save(path, SampleProject());

        var loaded = Repository.Load(path)!;

        Assert.Contains("Łódź", loaded.Settings.Budowa);
        Assert.Contains("Kraków", loaded.Settings.Wykonawca);
    }

    [Fact]
    public void Returns_nothing_for_a_project_that_does_not_exist()
        => Assert.Null(Repository.Load(Path_("brak.mpali")));

    [Fact]
    public void Returns_nothing_for_a_damaged_project_instead_of_throwing()
    {
        var path = Path_("uszkodzony.mpali");
        File.WriteAllText(path, "{ to nie jest json");

        Assert.Null(Repository.Load(path));
    }

    [Fact]
    public void Overwrites_a_previous_save()
    {
        var path = Path_("projekt.mpali");
        Repository.Save(path, SampleProject());

        var second = SampleProject();
        second.Piles[1].Executed = new DateTime(2022, 9, 13);
        Repository.Save(path, second);

        Assert.Equal(new DateTime(2022, 9, 13), Repository.Load(path)!.Piles[1].Executed);
    }

    [Fact]
    public void Leaves_no_temporary_file_behind()
    {
        var path = Path_("projekt.mpali");
        Repository.Save(path, SampleProject());

        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Creates_the_folder_when_it_is_missing()
    {
        var path = Path.Combine(_dir, "glebiej", "jeszcze", "projekt.mpali");

        Repository.Save(path, SampleProject());

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Backs_the_project_up_once_a_day()
    {
        var path = Path_("projekt.mpali");
        Repository.Save(path, SampleProject());

        Repository.BackupOnce(path);
        Repository.BackupOnce(path);      // same day again

        Assert.Single(Directory.GetFiles(_dir, "*.bak"));
    }

    [Fact]
    public void Backing_up_a_missing_project_does_nothing()
    {
        Repository.BackupOnce(Path_("brak.mpali"));

        Assert.Empty(Directory.GetFiles(_dir, "*.bak"));
    }

    [Fact]
    public void Default_project_lives_under_the_users_app_data()
    {
        Assert.EndsWith(".mpali", Repository.DefaultPath);
        Assert.Contains("MetrykiPali", Repository.DefaultPath);
    }

    [Fact]
    public void Each_piles_coefficient_is_saved_and_an_outstanding_pile_has_none()
    {
        var path = Path_("projekt.mpali");
        var project = SampleProject();
        project.Piles = new List<Pile>
        {
            new() { Number = 1, Executed = new DateTime(2022, 9, 12), ConcreteFactor = 1.18 },
            new() { Number = 2 }
        };

        Repository.Save(path, project);
        var loaded = Repository.Load(path)!;

        Assert.Equal(1.18, loaded.Piles[0].ConcreteFactor);
        Assert.Null(loaded.Piles[1].ConcreteFactor);
        Assert.DoesNotContain("\"ConcreteFactor\": null", File.ReadAllText(path));
    }

    // ---------------------------------------------------------------- sites

    [Fact]
    public void Sites_are_the_mpali_files_in_the_sites_folder_in_polish_order_backups_left_out()
    {
        var store = new JsonProjectRepository(_dir);
        foreach (var name in new[] { "Żoliborz", "Łódź", "Aleje", "Lublin" })
            store.Save(store.SitePath(name), SampleProject());
        store.BackupOnce(store.SitePath("Łódź"));

        Assert.Equal(new[] { "Aleje", "Lublin", "Łódź", "Żoliborz" }, store.ListSites());
    }

    [Fact]
    public void A_site_is_renamed_with_its_file_and_a_taken_name_is_refused()
    {
        var store = new JsonProjectRepository(_dir);
        store.Save(store.SitePath("Stara"), SampleProject());
        store.Save(store.SitePath("Inna"), SampleProject());
        store.LastSite = "Stara";

        store.RenameSite("Stara", "Nowa");

        Assert.Equal(new[] { "Inna", "Nowa" }, store.ListSites());
        Assert.Equal("Nowa", store.LastSite);
        Assert.NotNull(store.Load(store.SitePath("Nowa")));
        Assert.Throws<IOException>(() => store.RenameSite("Nowa", "Inna"));
    }

    [Fact]
    public void The_last_site_is_remembered_across_instances()
    {
        new JsonProjectRepository(_dir).LastSite = "Tuwima";

        Assert.Equal("Tuwima", new JsonProjectRepository(_dir).LastSite);
    }

    [Theory]
    [InlineData("  Tuwima   15  ", "Tuwima 15")]
    [InlineData("A/B\\C:D*E?F\"G<H>I|J", "A-B-C-D-E-F-G-H-I-J")]
    [InlineData("Łódź, ul. Tuwima.", "Łódź, ul. Tuwima")]
    [InlineData("   ", null)]
    [InlineData("///", null)]
    public void Site_names_are_made_safe_for_a_file_name(string typed, string? expected)
        => Assert.Equal(expected, SiteNames.Clean(typed));

    [Fact]
    public void A_name_already_taken_gets_a_number()
        => Assert.Equal("Tuwima (3)", SiteNames.Unique("Tuwima", new[] { "tuwima", "Tuwima (2)" }));

    /// <summary>
    /// A test build is given its own folder so it can never write over the real
    /// journal in %APPDATA%.
    /// </summary>
    [Fact]
    public void A_project_store_given_its_own_folder_keeps_the_journal_there()
    {
        var local = new JsonProjectRepository(_dir);

        local.Save(local.DefaultPath, SampleProject());

        Assert.Equal(Path_("projekt.mpali"), local.DefaultPath);
        Assert.True(File.Exists(Path_("projekt.mpali")));
        Assert.DoesNotContain(JsonProjectRepository.StandardDirectory, local.DefaultPath);
    }
}
