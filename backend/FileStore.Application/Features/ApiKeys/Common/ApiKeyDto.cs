namespace FileStore.Application.Features.ApiKeys.Common;

/// <summary>
/// Nunca expone KeyHash ni el valor completo. Prefix si: es publico por diseño
/// y es lo que permite reconocer la key en el panel.
/// </summary>
public record ApiKeyDto(
    Guid Id,
    string Name,
    string Prefix,
    int? RateLimitPerMinute,
    bool IsActive,
    DateTime? LastUsedAt,
    DateTime CreatedAt,
    DateTime? RevokedAt);

/// <summary>El Value viaja una unica vez, en la respuesta de creacion o rotacion.</summary>
public record CreateApiKeyResult(ApiKeyDto ApiKey, string Value);
