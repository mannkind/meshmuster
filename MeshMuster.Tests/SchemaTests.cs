namespace MeshMuster.Tests;

/// <summary>
/// The source and node migrations, exercised against a real SQLite file.
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
    public async Task Deleting_a_board_leaves_its_devices_alone() =>
        Assert.That(await this.Db.ForeignKeyDeleteAction("devices", "boards"),
            Is.EqualTo("SET NULL"));

    [Test]
    public async Task Deleting_a_release_leaves_the_device_version_label_intact() =>
        Assert.That(await this.Db.ForeignKeyDeleteAction("devices", "releases"),
            Is.EqualTo("SET NULL"));


    [Test]
    public async Task Migration_is_journalled_and_does_not_reapply()
    {
        await this.Db.MigrateAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await this.Db.MigrationMarked("001_sources.sql"), Is.True);
            Assert.That(await this.Db.MigrationMarked("002_nodes.sql"), Is.True);
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
