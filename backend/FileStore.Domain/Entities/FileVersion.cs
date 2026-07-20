namespace FileStore.Domain.Entities;

public class FileVersion
{
    public Guid Id { get; set; }
    public Guid FileId { get; set; }

    /// <summary>Correlativo por archivo, empieza en 1.</summary>
    public int VersionNumber { get; set; }

    /// <summary>Ruta relativa en disco: {clientId}/{yyyy}/{mm}/{fileVersionId}.bin</summary>
    public string StoragePath { get; set; } = null!;

    public long SizeBytes { get; set; }
    public string MimeType { get; set; } = null!;
    public string ChecksumSha256 { get; set; } = null!;

    /// <summary>Null si la subida vino del panel y no de la API.</summary>
    public Guid? UploadedByApiKeyId { get; set; }

    public DateTime CreatedAt { get; set; }

    public StoredFile File { get; set; } = null!;
    public ApiKey? UploadedByApiKey { get; set; }
}
