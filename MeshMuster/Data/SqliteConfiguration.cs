using MeshMuster.Config;

namespace MeshMuster.Data;

/// <summary>
/// The one place the SQLite connection string is spelled out.
/// </summary>
public static class SqliteConfiguration
{
    /// <summary>
    /// The connection string for the configured database.
    /// </summary>
    /// <param name="options"></param>
    public static string ConnectionString(AppOptions options) =>
        $"Data Source={options.DatabasePath};Mode=ReadWriteCreate;Cache=Shared;" +
        "Foreign Keys=True;Default Timeout=5";
}
