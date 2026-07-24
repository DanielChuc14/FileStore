namespace FileStore.Domain.Entities;

/// <summary>
/// Dueño operativo del servicio. No aparece explicitamente en la seccion 5 del
/// documento, pero es necesario: AuditLog referencia un ActorType.SuperAdmin y
/// AllowedFileType un UpdatedByAdminId. Se modela aparte de Client porque no
/// tiene cuota, ni almacenamiento, ni API Keys.
/// </summary>
public class SuperAdmin
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
