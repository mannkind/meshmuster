using MeshMuster.Data.Entities;
using MeshMuster.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MeshMuster.Tests;

/// <summary>
/// Reads the columns the way a stolen backup would, not the way the app does.
/// </summary>
public class SecretsAtRestTests
{
    [SetUp]
    public async Task SetUp()
    {
        this.Db = new SqliteTestDatabase();
        await this.Db.MigrateAsync();
    }

    [TearDown]
    public void TearDown() => this.Db.Dispose();

    [Test]
    public async Task The_database_file_holds_ciphertext_not_the_key()
    {
        const string key = "70B4941F5DB88E457C2E5D169ACB5CE33A6B66A05E57DEBC974AC89CC08E9279";
        const string password = "SECRET-PASSWORD";
        var id = await this.SaveDeviceAsync(key, password);

        var (storedKey, storedPassword) = await this.RawSecretsAsync(id);

        Assert.Multiple(() =>
        {
            Assert.That(storedKey, Does.Not.Contain(key));
            Assert.That(storedPassword, Does.Not.Contain(password));
            Assert.That(storedKey, Does.StartWith(SecretProtector.Prefix));
            Assert.That(storedPassword, Does.StartWith(SecretProtector.Prefix));
        });
    }

    [Test]
    public async Task The_app_still_reads_back_what_was_typed()
    {
        const string key = "70B4941F5DB88E457C2E5D169ACB5CE33A6B66A05E57DEBC974AC89CC08E9279";
        var id = await this.SaveDeviceAsync(key, "hunter2");

        await using var context = this.Db.NewContext();
        var device = await context.Devices.FirstAsync(d => d.Id == id);

        Assert.Multiple(() =>
        {
            Assert.That(device.PrivateKey, Is.EqualTo(key));
            Assert.That(device.AdminPassword, Is.EqualTo("hunter2"));
        });
    }

    [Test]
    public async Task A_blank_secret_stays_blank_rather_than_becoming_ciphertext()
    {
        var id = await this.SaveDeviceAsync(string.Empty, string.Empty);

        var (storedKey, storedPassword) = await this.RawSecretsAsync(id);

        Assert.Multiple(() =>
        {
            Assert.That(storedKey, Is.Empty);
            Assert.That(storedPassword, Is.Empty);
        });
    }

    [Test]
    public async Task A_row_written_before_encryption_fails_loudly()
    {
        var id = await this.SaveDeviceAsync("abc", "def");

        await using (var conn = new SqliteConnection(this.Db.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE devices SET private_key = 'PLAINTEXT' WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id.ToString());
            await cmd.ExecuteNonQueryAsync();
        }

        await using var context = this.Db.NewContext();

        Assert.That(async () => await context.Devices.FirstAsync(d => d.Id == id),
            Throws.Exception);
    }

    /// <summary>A device saved through EF; the only path that encrypts.</summary>
    /// <param name="privateKey"></param>
    /// <param name="adminPassword"></param>
    private async Task<Guid> SaveDeviceAsync(string privateKey, string adminPassword)
    {
        await using var context = this.Db.NewContext();
        var device = new Device
        {
            Id = Guid.NewGuid(),
            Name = "at-rest",
            Role = DeviceRole.Repeater,
            PrivateKey = privateKey,
            AdminPassword = adminPassword,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        context.Devices.Add(device);
        await context.SaveChangesAsync();
        return device.Id;
    }

    /// <summary>The two columns as SQLite has them, converter out of the way.</summary>
    /// <param name="id"></param>
    private async Task<(string Key, string Password)> RawSecretsAsync(Guid id)
    {
        await using var conn = new SqliteConnection(this.Db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT private_key, admin_password FROM devices WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetString(0), reader.GetString(1));
    }

    /// <summary>The database under test.</summary>
    private SqliteTestDatabase Db = null!;
}
