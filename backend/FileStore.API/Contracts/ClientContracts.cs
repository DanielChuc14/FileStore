namespace FileStore.API.Contracts;

public record CreateClientRequest(
    string Email,
    string Name,
    long QuotaBytes,
    int? TrashRetentionDays,
    long? MaxFileSizeBytes);

public record UpdateClientRequest(
    string? Name,
    long? QuotaBytes,
    bool? IsActive,
    int? TrashRetentionDays,
    long? MaxFileSizeBytes,
    bool ClearTrashRetentionOverride = false,
    bool ClearMaxFileSizeOverride = false);

/// <summary>La contraseña se muestra una unica vez; despues solo queda su hash.</summary>
public record GeneratedPasswordResponse(string Password);

public record CreateApiKeyRequest(string Name, int? RateLimitPerMinute);

public record UpdateApiKeyRequest(
    string? Name,
    int? RateLimitPerMinute,
    bool ClearRateLimitOverride = false);

public record UpdateConfigRequest(
    long? MaxFileSizeBytes,
    int? TrashRetentionDays,
    int? RateLimitDefaultPerMinute);

public record UpdateAllowedTypeRequest(bool IsEnabled);
