namespace FileStore.Application.Abstractions;

/// <summary>
/// Lee la configuracion global de la tabla AppConfigs. Cachea en memoria: estos
/// valores se consultan en cada subida y cambian muy de vez en cuando.
/// </summary>
public interface IAppConfigReader
{
    Task<long> GetMaxFileSizeBytesAsync(CancellationToken cancellationToken = default);

    Task<int> GetTrashRetentionDaysAsync(CancellationToken cancellationToken = default);

    Task<int> GetRateLimitDefaultPerMinuteAsync(CancellationToken cancellationToken = default);

    /// <summary>Extensiones habilitadas, en minusculas y sin punto.</summary>
    Task<IReadOnlySet<string>> GetAllowedExtensionsAsync(CancellationToken cancellationToken = default);

    Task<string?> GetMimeTypeForExtensionAsync(
        string extension,
        CancellationToken cancellationToken = default);

    /// <summary>Invalida el cache. Lo llama el super-admin al cambiar la configuracion.</summary>
    void Invalidate();
}
