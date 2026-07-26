using FileStore.Domain.Enums;

namespace FileStore.Domain.Entities;

/// <summary>
/// Token de recuperacion de contraseña. Igual que el refresh token, se guarda
/// solo el hash: quien lea la tabla no puede canjearlo.
///
/// Sirve tanto a clientes como al super-admin. Se identifica al dueño con
/// UserId + UserType y no con una FK, por el mismo motivo que RefreshToken:
/// apunta a dos tablas distintas.
/// </summary>
public class PasswordResetToken
{
    public Guid Id { get; set; }

    /// <summary>Id del Client o del SuperAdmin, segun UserType.</summary>
    public Guid UserId { get; set; }

    public UserType UserType { get; set; }

    public string TokenHash { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Cuando se canjeo. No nulo = ya usado, no se puede repetir.</summary>
    public DateTime? UsedAt { get; set; }

    public string? CreatedByIp { get; set; }

    public bool IsUsable => UsedAt is null && DateTime.UtcNow < ExpiresAt;
}
