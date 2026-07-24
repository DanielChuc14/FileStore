using FileStore.Application.Common;
using FileStore.Domain.Entities;

namespace FileStore.Infrastructure.Persistence.Configurations;

/// <summary>
/// Datos semilla estaticos, embebidos en la migracion via HasData.
/// Los Guid y la fecha son literales fijos a proposito: si se generaran en
/// tiempo de ejecucion, cada `dotnet ef migrations add` detectaria un cambio
/// y produciria una migracion espuria.
/// </summary>
internal static class SeedData
{
    private static readonly DateTime SeededAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static readonly AllowedFileType[] AllowedFileTypes =
    [
        New("a0000000-0000-0000-0000-000000000001", "jpg", "image/jpeg"),
        New("a0000000-0000-0000-0000-000000000002", "png", "image/png"),
        New("a0000000-0000-0000-0000-000000000003", "gif", "image/gif"),
        New("a0000000-0000-0000-0000-000000000004", "webp", "image/webp"),
        New("a0000000-0000-0000-0000-000000000005", "pdf", "application/pdf"),
        New("a0000000-0000-0000-0000-000000000006", "docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
        New("a0000000-0000-0000-0000-000000000007", "xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
        New("a0000000-0000-0000-0000-000000000008", "txt", "text/plain"),
        New("a0000000-0000-0000-0000-000000000009", "csv", "text/csv"),
    ];

    public static readonly AppConfig[] AppConfigs =
    [
        new() { Key = AppConfigKeys.MaxFileSizeBytes, Value = "10485760", UpdatedAt = SeededAt },
        new() { Key = AppConfigKeys.TrashRetentionDays, Value = "30", UpdatedAt = SeededAt },
        new() { Key = AppConfigKeys.RateLimitDefaultPerMinute, Value = "100", UpdatedAt = SeededAt },
    ];

    private static AllowedFileType New(string id, string extension, string mimeType) => new()
    {
        Id = Guid.Parse(id),
        Extension = extension,
        MimeType = mimeType,
        IsEnabled = true,
        UpdatedByAdminId = null,
        UpdatedAt = SeededAt
    };
}
