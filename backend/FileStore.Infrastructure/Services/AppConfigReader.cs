using FileStore.Application.Abstractions;
using FileStore.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FileStore.Infrastructure.Services;

public class AppConfigReader(IApplicationDbContext context, IMemoryCache cache) : IAppConfigReader
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private const string ConfigCacheKey = "app-config";
    private const string ExtensionsCacheKey = "allowed-extensions";

    public async Task<long> GetMaxFileSizeBytesAsync(CancellationToken cancellationToken = default) =>
        await GetLongAsync(AppConfigKeys.MaxFileSizeBytes, 10 * 1024 * 1024, cancellationToken);

    public async Task<int> GetTrashRetentionDaysAsync(CancellationToken cancellationToken = default) =>
        (int)await GetLongAsync(AppConfigKeys.TrashRetentionDays, 30, cancellationToken);

    public async Task<int> GetRateLimitDefaultPerMinuteAsync(CancellationToken cancellationToken = default) =>
        (int)await GetLongAsync(AppConfigKeys.RateLimitDefaultPerMinute, 100, cancellationToken);

    public async Task<IReadOnlySet<string>> GetAllowedExtensionsAsync(
        CancellationToken cancellationToken = default)
    {
        var map = await GetExtensionMapAsync(cancellationToken);
        return map.Keys.ToHashSet();
    }

    public async Task<string?> GetMimeTypeForExtensionAsync(
        string extension,
        CancellationToken cancellationToken = default)
    {
        var map = await GetExtensionMapAsync(cancellationToken);
        return map.GetValueOrDefault(extension.ToLowerInvariant());
    }

    public void Invalidate()
    {
        cache.Remove(ConfigCacheKey);
        cache.Remove(ExtensionsCacheKey);
    }

    private async Task<Dictionary<string, string>> GetExtensionMapAsync(
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(ExtensionsCacheKey, out Dictionary<string, string>? cached) &&
            cached is not null)
        {
            return cached;
        }

        var map = await context.AllowedFileTypes
            .AsNoTracking()
            .Where(t => t.IsEnabled)
            .ToDictionaryAsync(t => t.Extension.ToLower(), t => t.MimeType, cancellationToken);

        cache.Set(ExtensionsCacheKey, map, CacheDuration);
        return map;
    }

    private async Task<long> GetLongAsync(
        string key,
        long fallback,
        CancellationToken cancellationToken)
    {
        var values = await GetAllAsync(cancellationToken);

        return values.TryGetValue(key, out var raw) && long.TryParse(raw, out var parsed)
            ? parsed
            : fallback;
    }

    private async Task<Dictionary<string, string>> GetAllAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(ConfigCacheKey, out Dictionary<string, string>? cached) &&
            cached is not null)
        {
            return cached;
        }

        var values = await context.AppConfigs
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Key, c => c.Value, cancellationToken);

        cache.Set(ConfigCacheKey, values, CacheDuration);
        return values;
    }
}
