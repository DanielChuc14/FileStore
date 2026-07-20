namespace FileStore.Domain.Entities;

/// <summary>
/// El documento la llama "File". Se renombro a StoredFile porque System.IO.File
/// entra por ImplicitUsings y el nombre quedaria ambiguo en cada archivo que la use.
/// </summary>
public class StoredFile
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }

    /// <summary>Null = raiz del cliente.</summary>
    public Guid? FolderId { get; set; }

    public string OriginalName { get; set; } = null!;

    /// <summary>
    /// Version vigente. No siempre es la de mayor VersionNumber: restaurar una
    /// version antigua reapunta este campo sin crear una nueva.
    /// </summary>
    public Guid? CurrentVersionId { get; set; }

    public long SizeBytes { get; set; }
    public string MimeType { get; set; } = null!;
    public string Extension { get; set; } = null!;

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Client Client { get; set; } = null!;
    public Folder? Folder { get; set; }
    public FileVersion? CurrentVersion { get; set; }
    public ICollection<FileVersion> Versions { get; set; } = [];
}
