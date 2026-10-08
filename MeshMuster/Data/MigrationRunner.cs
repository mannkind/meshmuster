using System.Runtime.ExceptionServices;
using DbUp;
using Microsoft.Data.Sqlite;

namespace MeshMuster.Data;

/// <summary>
/// Applies the shipped SQL migrations to the database.
/// </summary>
public class MigrationRunner
{
    /// <summary>
    /// Initializes a new instance of the MigrationRunner class.
    /// </summary>
    /// <param name="connectionString"></param>
    /// <param name="migrationDirectory"></param>
    /// <param name="logger"></param>
    public MigrationRunner(
        string connectionString,
        string migrationDirectory,
        ILogger<MigrationRunner> logger)
    {
        this.ConnectionString = connectionString;
        this.MigrationDirectory = migrationDirectory;
        this.Logger = logger;
    }

    /// <summary>
    /// Bring the database up to the newest migration.
    /// </summary>
    /// <param name="ct"></param>
    public async Task RunAsync(CancellationToken ct = default)
    {
        await this.EnableWalAsync(ct);

        var result = DeployChanges.To
            .SqliteDatabase(this.ConnectionString)
            .WithScriptsFromFileSystem(this.MigrationDirectory)
            .WithTransactionPerScript()
            .LogToNowhere()
            .Build()
            .PerformUpgrade();

        foreach (var script in result.Scripts)
        {
            this.Logger.LogInformation("Applied migration {Name}", script.Name);
        }

        if (!result.Successful)
        {
            ExceptionDispatchInfo.Capture(result.Error).Throw();
        }
    }

    /// <summary>
    /// The connection string for the database being migrated.
    /// </summary>
    private readonly string ConnectionString;

    /// <summary>
    /// The directory the migration scripts are read from.
    /// </summary>
    private readonly string MigrationDirectory;

    /// <summary>
    /// The logger used internally.
    /// </summary>
    private readonly ILogger<MigrationRunner> Logger;

    /// <summary>
    /// WAL so a reader and the poller don't lock each other out.
    /// </summary>
    /// <param name="ct"></param>
    private async Task EnableWalAsync(CancellationToken ct)
    {
        await using var conn = new SqliteConnection(this.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL;";
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
