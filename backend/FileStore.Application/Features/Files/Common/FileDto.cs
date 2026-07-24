namespace FileStore.Application.Features.Files.Common;

public record FileDto(
    Guid Id,
    Guid? FolderId,
    string OriginalName,
    long SizeBytes,
    string MimeType,
    string Extension,
    int VersionCount,
    bool IsDeleted,
    DateTime? DeletedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Contenido listo para enviar por streaming, sin cargarlo en memoria.</summary>
public record FileDownload(Stream Content, string FileName, string MimeType, long SizeBytes);
