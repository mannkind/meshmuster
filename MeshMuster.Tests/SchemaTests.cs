namespace MeshMuster.Tests;

/// <summary>
/// The source migrations, exercised against a real SQLite file.
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
    public async Task Creates_source_tables(string table) =>
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
    public async Task Migration_is_journalled_and_does_not_reapply()
    {
        await this.Db.MigrateAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await this.Db.MigrationMarked("001_sources.sql"), Is.True);
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
