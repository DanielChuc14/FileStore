namespace FileStore.Domain.Entities;

/// <summary>
/// Token de recuperacion de contraseña. Igual que el refresh token, se guarda
/// solo el hash: quien lea la tabla no puede canjearlo.
///
/// Solo aplica a clientes. El super-admin no tiene recuperacion por correo a
/// proposito: su cuenta se resiembra desde los secretos del servidor, y un
/// endpoint publico que pudiera resetear al administrador seria un blanco
/// demasiado goloso.
/// </summary>
public class PasswordResetToken
{
    public Guid Id { get; set; }

    public Guid ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public string TokenHash { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Cuando se canjeo. No nulo = ya usado, no se puede repetir.</summary>
    public DateTime? UsedAt { get; set; }

    public string? CreatedByIp { get; set; }

    public bool IsUsable => UsedAt is null && DateTime.UtcNow < ExpiresAt;
}
