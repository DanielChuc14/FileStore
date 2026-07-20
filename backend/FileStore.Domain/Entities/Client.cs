namespace FileStore.Domain.Entities;

public class Client
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string Name { get; set; } = null!;

    public long QuotaBytes { get; set; }
    public long UsedBytes { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Override opcional del default global. Null = usa el valor de AppConfig.</summary>
    public int? TrashRetentionDays { get; set; }

    /// <summary>Override opcional del default global. Null = usa el valor de AppConfig.</summary>
    public long? MaxFileSizeBytes { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ApiKey> ApiKeys { get; set; } = [];
    public ICollection<Folder> Folders { get; set; } = [];
    public ICollection<StoredFile> Files { get; set; } = [];
}
