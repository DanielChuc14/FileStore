using FileStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Abstractions;

/// <summary>
/// Acceso a datos visto desde Application. Expone DbSet y no repositorios por
/// entidad: para este volumen, un repositorio por tabla seria mucho codigo sin
/// beneficio. Lo que si gana Application es no depender de la clase concreta
/// FileStoreDbContext, que vive en Infrastructure.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Client> Clients { get; }
    DbSet<SuperAdmin> SuperAdmins { get; }
    DbSet<ApiKey> ApiKeys { get; }
    DbSet<Folder> Folders { get; }
    DbSet<StoredFile> Files { get; }
    DbSet<FileVersion> FileVersions { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<AllowedFileType> AllowedFileTypes { get; }
    DbSet<AppConfig> AppConfigs { get; }
    DbSet<RefreshToken> RefreshTokens { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Transaccion explicita. Hace falta cuando una operacion necesita mas de un
    /// SaveChanges para resolver dependencias circulares entre entidades y aun
    /// asi tiene que ser atomica.
    /// </summary>
    Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken = default);

    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
}
