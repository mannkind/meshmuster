using Microsoft.Data.Sqlite;

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

    [Test]
    public async Task Migration_002_clears_mikecarper_etag_and_stale_utility_releases_only()
    {
        using var db = new SqliteTestDatabase();
        var shipped = await File.ReadAllTextAsync(
            Path.Combine(db.ProductionMigrationsDir, "001_init.sql"));
        db.WriteMigration("001_init.sql", shipped);
        await db.Runner().RunAsync();

        await Execute(db.ConnectionString, """
            UPDATE sources SET etag = 'W/"mike"' WHERE slug = 'meshcore-mikecarper';
            UPDATE sources SET etag = 'W/"official"' WHERE slug = 'meshcore-official';
            INSERT INTO releases (id, stream_id, github_id, tag, version_label, sort_key, published_at) VALUES
              ('r-mike-sensor-117',    'b1000000-0000-4000-8000-000000000009', 1, 'utility-v1.17.0',  '1.17.0', '00001.00017.00000.00000', '2026-01-01'),
              ('r-mike-sensor-118',    'b1000000-0000-4000-8000-000000000009', 2, 'utility-v1.18.0',  '1.18.0', '00001.00018.00000.00000', '2026-02-01'),
              ('r-mike-sensor-119',    'b1000000-0000-4000-8000-000000000009', 3, 'utility-v1.19.1',  '1.19.1', '00001.00019.00001.00000', '2026-03-01'),
              ('r-official-sensor-118','b1000000-0000-4000-8000-000000000011', 4, 'utility-v1.18.0',  '1.18.0', '00001.00018.00000.00000', '2026-02-01'),
              ('r-mike-repeater-118',  'b1000000-0000-4000-8000-000000000003', 5, 'repeater-v1.18.0', '1.18.0', '00001.00018.00000.00000', '2026-02-01');
            INSERT INTO devices (id, name, role, firmware_release_id, firmware_version_label, firmware_sort_key)
              VALUES ('d1', 'Node', 'sensor', 'r-mike-sensor-118', '1.18.0', '00001.00018.00000.00000');
            INSERT INTO device_flashes (id, device_id, occurred_at, firmware_to_label)
              VALUES ('f1', 'd1', '2026-02-02', '1.18.0');
            """);

        await db.MigrateAsync();

        var mikeEtag = await Strings(db.ConnectionString,
            "SELECT etag FROM sources WHERE slug = 'meshcore-mikecarper'");
        var officialEtag = await Strings(db.ConnectionString,
            "SELECT etag FROM sources WHERE slug = 'meshcore-official'");
        var releases = await Strings(db.ConnectionString,
            "SELECT id FROM releases ORDER BY id");
        var deviceRelease = await Strings(db.ConnectionString,
            "SELECT firmware_release_id FROM devices WHERE id = 'd1'");
        var deviceLabel = await Strings(db.ConnectionString,
            "SELECT firmware_version_label FROM devices WHERE id = 'd1'");

        Assert.Multiple(async () =>
        {
            Assert.That(mikeEtag, Is.EqualTo(new[] { "" }));
            Assert.That(officialEtag, Is.EqualTo(new[] { "W/\"official\"" }));
            Assert.That(releases, Is.EqualTo(new[]
            {
                "r-mike-repeater-118", "r-mike-sensor-117", "r-official-sensor-118",
            }));
            Assert.That(deviceRelease, Is.EqualTo(new string?[] { null }));
            Assert.That(deviceLabel, Is.EqualTo(new[] { "1.18.0" }));
            Assert.That(await db.Count("device_flashes"), Is.EqualTo(1));
        });
    }

    /// <summary>
    /// Runs a batch of statements against a database.
    /// </summary>
    /// <param name="connectionString"></param>
    /// <param name="sql"></param>
    private static async Task Execute(string connectionString, string sql)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// The first column of a query, one entry per row; null for SQL NULL.
    /// </summary>
    /// <param name="connectionString"></param>
    /// <param name="sql"></param>
    private static async Task<List<string?>> Strings(string connectionString, string sql)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<string?>();
        while (await reader.ReadAsync())
            rows.Add(reader.IsDBNull(0) ? null : reader.GetString(0));
        return rows;
    }

    /// <summary>
    /// The database under test.
    /// </summary>
    private SqliteTestDatabase Db = null!;
}
