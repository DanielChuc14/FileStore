namespace FileStore.Domain.Entities;

public class Folder
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }

    /// <summary>Null = carpeta raiz del cliente.</summary>
    public Guid? ParentFolderId { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>
    /// Ruta completa cacheada (ej: /docs/2025/facturas). Es un denormalizado:
    /// al renombrar o mover una carpeta hay que recalcularlo en toda la descendencia.
    /// </summary>
    public string Path { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public Client Client { get; set; } = null!;
    public Folder? ParentFolder { get; set; }
    public ICollection<Folder> ChildFolders { get; set; } = [];
    public ICollection<StoredFile> Files { get; set; } = [];
}
