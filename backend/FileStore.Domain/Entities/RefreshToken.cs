using FileStore.Domain.Enums;

namespace FileStore.Domain.Entities;

/// <summary>
/// Refresh token persistido. Se guarda solo el hash: si alguien lee la tabla no
/// puede reconstruir el token y suplantar la sesion.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }

    /// <summary>Id del Client o del SuperAdmin. No hay FK porque apunta a dos tablas distintas.</summary>
    public Guid UserId { get; set; }

    public UserType UserType { get; set; }

    public string TokenHash { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Token que reemplazo a este durante la rotacion. Encadena la familia de
    /// tokens: si llega uno ya revocado, se sigue la cadena para revocarla entera.
    /// </summary>
    public Guid? ReplacedByTokenId { get; set; }

    public string? CreatedByIp { get; set; }

    public bool IsActive => RevokedAt is null && DateTime.UtcNow < ExpiresAt;
}
