namespace FileStore.Application.Common;

/// <summary>
/// Claves de la tabla AppConfigs. Viven en Application porque las usan tanto los
/// handlers como el seed de Infrastructure; al reves, Application no puede ver
/// Infrastructure.
/// </summary>
public static class AppConfigKeys
{
    public const string MaxFileSizeBytes = "MaxFileSizeBytes";
    public const string TrashRetentionDays = "TrashRetentionDays";
    public const string RateLimitDefaultPerMinute = "RateLimitDefaultPerMinute";
}
