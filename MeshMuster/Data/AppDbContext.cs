using System.Globalization;
using MeshMuster.Data.Entities;
using MeshMuster.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MeshMuster.Data;

/// <summary>
/// The EF Core context
/// </summary>
public class AppDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the AppDbContext class.
    /// </summary>
    /// <param name="options"></param>
    /// <param name="secrets"></param>
    public AppDbContext(DbContextOptions<AppDbContext> options, SecretProtector secrets) :
        base(options)
    {
        this.Secrets = secrets;
    }

    public DbSet<Source> Sources => Set<Source>();
    public DbSet<ReleaseStream> Streams => Set<ReleaseStream>();
    public DbSet<Release> Releases => Set<Release>();
    public DbSet<ReleaseAsset> ReleaseAssets => Set<ReleaseAsset>();
    public DbSet<Board> Boards => Set<Board>();
    public DbSet<BoardAssetPattern> BoardAssetPatterns => Set<BoardAssetPattern>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceFlash> DeviceFlashes => Set<DeviceFlash>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Guid>().HaveConversion<string>();
        configurationBuilder.Properties<Guid?>().HaveConversion<string?>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        var dateTime = new ValueConverter<DateTime, string>(
            d => d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            s => DateTime.ParseExact(s, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

        var nullableDateTime = new ValueConverter<DateTime?, string?>(
            d => d == null ? null : d.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            s => s == null ? null : DateTime.ParseExact(s, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

        // Nothing queries these two, so SQLite never has to filter over ciphertext.
        var protector = this.Secrets;
        var secret = new ValueConverter<string, string>(
            v => protector.Protect(v),
            v => protector.Unprotect(v));

        var dateOnly = new ValueConverter<DateOnly, string>(
            d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            s => DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture));

        b.Entity<Source>(e =>
        {
            e.ToTable("sources");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Slug).HasColumnName("slug");
            e.Property(x => x.Kind).HasColumnName("kind");
            e.Property(x => x.DisplayName).HasColumnName("display_name");
            e.Property(x => x.GithubOwner).HasColumnName("github_owner");
            e.Property(x => x.GithubRepo).HasColumnName("github_repo");
            e.Property(x => x.IncludePrereleases).HasColumnName("include_prereleases").HasConversion<int>();
            e.Property(x => x.IsEnabled).HasColumnName("is_enabled").HasConversion<int>();
            e.Property(x => x.Etag).HasColumnName("etag");
            e.Property(x => x.LastPolledAt).HasColumnName("last_polled_at").HasConversion(nullableDateTime);
            e.Property(x => x.LastPollError).HasColumnName("last_poll_error");
            e.Property(x => x.SortOrder).HasColumnName("sort_order");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasConversion(dateTime);
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasConversion(dateTime);
            e.Ignore(x => x.RepoSlug);
            e.Ignore(x => x.HtmlUrl);
            e.HasIndex(x => x.Slug).IsUnique();
        });

        b.Entity<ReleaseStream>(e =>
        {
            e.ToTable("streams");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.SourceId).HasColumnName("source_id");
            e.Property(x => x.Slug).HasColumnName("slug");
            e.Property(x => x.DisplayName).HasColumnName("display_name");
            e.Property(x => x.DeviceRole).HasColumnName("device_role");
            e.HasOne(x => x.Source).WithMany(x => x.Streams)
                .HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Release>(e =>
        {
            e.ToTable("releases");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.StreamId).HasColumnName("stream_id");
            e.Property(x => x.GithubId).HasColumnName("github_id");
            e.Property(x => x.Tag).HasColumnName("tag");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.VersionLabel).HasColumnName("version_label");
            e.Property(x => x.SortKey).HasColumnName("sort_key");
            e.Property(x => x.Detail).HasColumnName("detail");
            e.Property(x => x.IsPrerelease).HasColumnName("is_prerelease").HasConversion<int>();
            e.Property(x => x.PublishedAt).HasColumnName("published_at").HasConversion(dateTime);
            e.Property(x => x.HtmlUrl).HasColumnName("html_url");
            e.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at").HasConversion(dateTime);
            e.HasOne(x => x.Stream).WithMany(x => x.Releases)
                .HasForeignKey(x => x.StreamId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.StreamId, x.GithubId }).IsUnique();
        });

        b.Entity<ReleaseAsset>(e =>
        {
            e.ToTable("release_assets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ReleaseId).HasColumnName("release_id");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.DownloadUrl).HasColumnName("download_url");
            e.Property(x => x.SizeBytes).HasColumnName("size_bytes");
            e.HasOne(x => x.Release).WithMany(x => x.Assets)
                .HasForeignKey(x => x.ReleaseId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Board>(e =>
        {
            e.ToTable("boards");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.IsActive).HasColumnName("is_active").HasConversion<int>();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasConversion(dateTime);
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasConversion(dateTime);
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<BoardAssetPattern>(e =>
        {
            e.ToTable("board_asset_patterns");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.BoardId).HasColumnName("board_id");
            e.Property(x => x.SourceId).HasColumnName("source_id");
            e.Property(x => x.Pattern).HasColumnName("pattern");
            e.HasOne(x => x.Board).WithMany(x => x.AssetPatterns)
                .HasForeignKey(x => x.BoardId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Source).WithMany(x => x.BoardAssetPatterns)
                .HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.BoardId, x.SourceId }).IsUnique();
        });

        b.Entity<Device>(e =>
        {
            e.ToTable("devices");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Role).HasColumnName("role");
            e.Property(x => x.BoardId).HasColumnName("board_id");
            e.Property(x => x.FirmwareStreamId).HasColumnName("firmware_stream_id");
            e.Property(x => x.FirmwareReleaseId).HasColumnName("firmware_release_id");
            e.Property(x => x.FirmwareVersionLabel).HasColumnName("firmware_version_label");
            e.Property(x => x.FirmwareSortKey).HasColumnName("firmware_sort_key");
            e.Property(x => x.BootloaderStreamId).HasColumnName("bootloader_stream_id");
            e.Property(x => x.BootloaderReleaseId).HasColumnName("bootloader_release_id");
            e.Property(x => x.BootloaderVersionLabel).HasColumnName("bootloader_version_label");
            e.Property(x => x.BootloaderSortKey).HasColumnName("bootloader_sort_key");
            e.Property(x => x.Latitude).HasColumnName("latitude");
            e.Property(x => x.Longitude).HasColumnName("longitude");
            e.Ignore(x => x.HasLocation);
            e.Property(x => x.PrivateKey).HasColumnName("private_key").HasConversion(secret);
            e.Property(x => x.AdminPassword).HasColumnName("admin_password").HasConversion(secret);
            e.Property(x => x.Notes).HasColumnName("notes");
            e.Property(x => x.IsActive).HasColumnName("is_active").HasConversion<int>();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasConversion(dateTime);
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasConversion(dateTime);

            e.HasOne(x => x.Board).WithMany(x => x.Devices)
                .HasForeignKey(x => x.BoardId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.FirmwareStream).WithMany()
                .HasForeignKey(x => x.FirmwareStreamId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.FirmwareRelease).WithMany()
                .HasForeignKey(x => x.FirmwareReleaseId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.BootloaderStream).WithMany()
                .HasForeignKey(x => x.BootloaderStreamId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.BootloaderRelease).WithMany()
                .HasForeignKey(x => x.BootloaderReleaseId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<DeviceFlash>(e =>
        {
            e.ToTable("device_flashes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.DeviceId).HasColumnName("device_id");
            e.Property(x => x.OccurredAt).HasColumnName("occurred_at").HasConversion(dateOnly);
            e.Property(x => x.FirmwareFromLabel).HasColumnName("firmware_from_label");
            e.Property(x => x.FirmwareToLabel).HasColumnName("firmware_to_label");
            e.Property(x => x.BootloaderFromLabel).HasColumnName("bootloader_from_label");
            e.Property(x => x.BootloaderToLabel).HasColumnName("bootloader_to_label");
            e.Property(x => x.Note).HasColumnName("note");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasConversion(dateTime);
            e.Ignore(x => x.ChangedFirmware);
            e.Ignore(x => x.ChangedBootloader);
            e.HasOne(x => x.Device).WithMany(x => x.Flashes)
                .HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    /// <summary>
    /// The protector.
    /// </summary>
    internal SecretProtector SecretsForCacheKey => this.Secrets;

    /// <summary>
    /// Encrypts the secret columns.
    /// </summary>
    private readonly SecretProtector Secrets;
}

/// <summary>
/// Keys the cached model on the protector as well as the context type.
/// </summary>
public class SecretModelCacheKeyFactory : IModelCacheKeyFactory
{
    /// <inheritdoc />
    public object Create(DbContext context, bool designTime) =>
        context is AppDbContext app
            ? (context.GetType(), app.SecretsForCacheKey, designTime)
            : (object)(context.GetType(), designTime);
}
