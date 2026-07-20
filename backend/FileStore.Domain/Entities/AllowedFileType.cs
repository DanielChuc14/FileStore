namespace FileStore.Domain.Entities;

public class AllowedFileType
{
    public Guid Id { get; set; }

    /// <summary>Sin punto y en minusculas: jpg, pdf, docx.</summary>
    public string Extension { get; set; } = null!;

    public string MimeType { get; set; } = null!;
    public bool IsEnabled { get; set; } = true;

    /// <summary>Null en las filas del seed inicial.</summary>
    public Guid? UpdatedByAdminId { get; set; }

    public DateTime UpdatedAt { get; set; }

    public SuperAdmin? UpdatedByAdmin { get; set; }
}
