using MeshMuster.Config;
using MeshMuster.Data;
using MeshMuster.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeshMuster.Tests;

/// <summary>
/// A real SQLite file in a temp directory, migrated and thrown away per test.
/// </summary>
public class SqliteTestDatabase : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the SqliteTestDatabase class.
    /// </summary>
    public SqliteTestDatabase()
    {
        this.RootDir = Directory.CreateTempSubdirectory("meshmuster-tests-").FullName;
        var dbPath = Path.Combine(this.RootDir, "test.db");
        this.ConnectionString =
            $"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared;Foreign Keys=True;Default Timeout=5";

        this.ScratchMigrationDir = Path.Combine(this.RootDir, "scratch-migrations");
        Directory.CreateDirectory(this.ScratchMigrationDir);

        this.ProductionMigrationsDir = Path.Combine(AppContext.BaseDirectory, "Data", "Migrations");
        this.Options = new AppOptions
        {
            StatePath = this.RootDir,
            DatabasePath = dbPath,
            SecretKey = TestSecretKey,
        };
        this.Secrets = new SecretProtector(this.Options.SecretKeyBytes()!);
    }

    public string ConnectionString { get; }

    /// <summary>The migrations that ship, copied in by the csproj.</summary>
    public string ProductionMigrationsDir { get; }

    public AppOptions Options { get; }

    /// <summary>The protector the contexts here are built with.</summary>
    public SecretProtector Secrets { get; }

    /// <summary>A fixed key, so a test can assert on what landed in the column.</summary>
    public const string TestSecretKey = "bWVzaG11c3Rlci11bml0LXRlc3Qta2V5LTMyYnl0ZXM=";

    /// <summary>
    /// Write a one-off migration for a test to run.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="sql"></param>
    public void WriteMigration(string name, string sql) =>
        File.WriteAllText(Path.Combine(this.ScratchMigrationDir, name), sql);

    /// <summary>
    /// A runner over the scratch migrations.
    /// </summary>
    public MigrationRunner Runner() =>
        new(this.ConnectionString, this.ScratchMigrationDir, NullLogger<MigrationRunner>.Instance);

    /// <summary>
    /// A runner over the migrations that ship.
    /// </summary>
    public MigrationRunner RunnerWithAllMigrations() =>
        new(this.ConnectionString, this.ProductionMigrationsDir, NullLogger<MigrationRunner>.Instance);

    /// <summary>
    /// Bring the database up to the newest shipped migration.
    /// </summary>
    public Task MigrateAsync() => this.RunnerWithAllMigrations().RunAsync();

    /// <summary>
    /// A context over the test database; disposed with it.
    /// </summary>
    public AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(this.ConnectionString)
            .ReplaceService<IModelCacheKeyFactory, SecretModelCacheKeyFactory>()
            .Options;
        var context = new AppDbContext(options, this.Secrets);
        this.Contexts.Add(context);
        return context;
    }

    /// <summary>
    /// Whether a table exists.
    /// </summary>
    /// <param name="name"></param>
    public async Task<bool> TableExists(string name)
    {
        await using var conn = new SqliteConnection(this.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name";
        cmd.Parameters.AddWithValue("@name", name);
        return (long)(await cmd.ExecuteScalarAsync())! > 0;
    }

    /// <summary>
    /// Whether an index exists.
    /// </summary>
    /// <param name="name"></param>
    public async Task<bool> IndexExists(string name)
    {
        await using var conn = new SqliteConnection(this.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = @name";
        cmd.Parameters.AddWithValue("@name", name);
        return (long)(await cmd.ExecuteScalarAsync())! > 0;
    }

    /// <summary>
    /// Whether DbUp journalled a migration.
    /// </summary>
    /// <param name="name"></param>
    public async Task<bool> MigrationMarked(string name)
    {
        if (!await this.TableExists("SchemaVersions")) return false;

        await using var conn = new SqliteConnection(this.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM SchemaVersions WHERE ScriptName = @name";
        cmd.Parameters.AddWithValue("@name", name);
        return (long)(await cmd.ExecuteScalarAsync())! > 0;
    }

    /// <summary>
    /// Rows in a table.
    /// </summary>
    /// <param name="table"></param>
    public async Task<long> Count(string table)
    {
        await using var conn = new SqliteConnection(this.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// The ON DELETE a foreign key carries, straight out of the pragma.
    /// </summary>
    /// <param name="table"></param>
    /// <param name="referencedTable"></param>
    public async Task<string?> ForeignKeyDeleteAction(string table, string referencedTable)
    {
        await using var conn = new SqliteConnection(this.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA foreign_key_list({table})";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var referenced = reader.GetString(reader.GetOrdinal("table"));
            if (referenced.Equals(referencedTable, StringComparison.OrdinalIgnoreCase))
                return reader.GetString(reader.GetOrdinal("on_delete"));
        }
        return null;
    }

    /// <summary>
    /// The journal mode in force.
    /// </summary>
    public async Task<string> JournalMode()
    {
        await using var conn = new SqliteConnection(this.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode";
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var context in this.Contexts) context.Dispose();

        // Pooled connections keep the file open, and Windows won't delete it under them.
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(this.RootDir, recursive: true);
        }
        catch (IOException)
        {
            // Not worth failing a run over a temp directory.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The temp directory holding the database and the scratch migrations.
    /// </summary>
    private readonly string RootDir;

    /// <summary>
    /// Where a test's one-off migrations are written.
    /// </summary>
    private readonly string ScratchMigrationDir;

    /// <summary>
    /// The contexts handed out, disposed with this.
    /// </summary>
    private readonly List<AppDbContext> Contexts = [];
}
