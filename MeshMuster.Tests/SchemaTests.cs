namespace MeshMuster.Tests;

/// <summary>
/// The migrations, exercised against a real SQLite file.
/// </summary>
public class SchemaTests
{
    [SetUp]
    public async Task SetUp()
    {
        this.Db = new SqliteTestDatabase();
        await this.Db.MigrateAsync();
    }

    [TearDown]
    public void TearDown() => this.Db.Dispose();

    [TestCase("sources")]
    [TestCase("streams")]
    [TestCase("releases")]
    [TestCase("release_assets")]
    [TestCase("boards")]
    [TestCase("board_asset_patterns")]
    [TestCase("devices")]
    [TestCase("device_flashes")]
    public async Task Creates_every_table(string table) =>
        Assert.That(await this.Db.TableExists(table), Is.True);

    [Test]
    public async Task Seeds_the_four_sources_and_twelve_streams()
    {
        Assert.Multiple(async () =>
        {
            Assert.That(await this.Db.Count("sources"), Is.EqualTo(4));
            Assert.That(await this.Db.Count("streams"), Is.EqualTo(12));
        });
    }

    [Test]
    public async Task Forks_include_prereleases_and_official_does_not()
    {
        await using var db = this.Db.NewContext();

        var official = db.Sources.Single(s => s.Slug == "meshcore-official");
        var mikecarper = db.Sources.Single(s => s.Slug == "meshcore-mikecarper");

        Assert.Multiple(() =>
        {
            Assert.That(mikecarper.IncludePrereleases, Is.True);
            Assert.That(official.IncludePrereleases, Is.False);
        });
    }

    [Test]
    public async Task Deleting_a_device_takes_its_flash_history_with_it() =>
        Assert.That(await this.Db.ForeignKeyDeleteAction("device_flashes", "devices"),
            Is.EqualTo("CASCADE"));

    [Test]
    public async Task Deleting_a_board_leaves_its_devices_alone() =>
        Assert.That(await this.Db.ForeignKeyDeleteAction("devices", "boards"),
            Is.EqualTo("SET NULL"));

    [Test]
    public async Task Deleting_a_release_leaves_the_device_version_label_intact() =>
        Assert.That(await this.Db.ForeignKeyDeleteAction("devices", "releases"),
            Is.EqualTo("SET NULL"));

    [Test]
    public async Task Sorting_indexes_exist()
    {
        Assert.Multiple(async () =>
        {
            Assert.That(await this.Db.IndexExists("ix_devices_firmware_sort"), Is.True);
            Assert.That(await this.Db.IndexExists("ix_devices_bootloader_sort"), Is.True);
            Assert.That(await this.Db.IndexExists("ix_releases_stream_sort"), Is.True);
        });
    }

    [Test]
    public async Task Migrations_are_journalled_and_do_not_reapply()
    {
        await this.Db.MigrateAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await this.Db.MigrationMarked("001_init.sql"), Is.True);
            // Running twice must not seed a second copy of the four sources.
            Assert.That(await this.Db.Count("sources"), Is.EqualTo(4));
            Assert.That(await this.Db.Count("streams"), Is.EqualTo(12));
        });
    }

    [Test]
    public async Task Enables_wal() =>
        Assert.That(await this.Db.JournalMode(), Is.EqualTo("wal").IgnoreCase);

    /// <summary>
    /// The database under test.
    /// </summary>
    private SqliteTestDatabase Db = null!;
}
