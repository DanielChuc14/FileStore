using FileStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Infrastructure.Persistence;

public class FileStoreDbContext(DbContextOptions<FileStoreDbContext> options) : DbContext(options)
{
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<SuperAdmin> SuperAdmins => Set<SuperAdmin>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<StoredFile> Files => Set<StoredFile>();
    public DbSet<FileVersion> FileVersions => Set<FileVersion>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AllowedFileType> AllowedFileTypes => Set<AllowedFileType>();
    public DbSet<AppConfig> AppConfigs => Set<AppConfig>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FileStoreDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
