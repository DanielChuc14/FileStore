using FileStore.Domain.Enums;

namespace FileStore.Application.Abstractions;

/// <summary>
/// Quien esta ejecutando el request. Application no puede leer HttpContext
/// (no conoce ASP.NET), asi que la capa API lo implementa a partir de los claims.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    /// <summary>
    /// Cliente propietario de los datos del request. Se resuelve del claim
    /// client_id, que emiten tanto el JWT de rol Client como el esquema de API
    /// Key. Es null para el super-admin: no es dueño de ningun contenido.
    /// </summary>
    Guid? ClientId { get; }

    UserType? UserType { get; }
    string? Email { get; }

    /// <summary>API Key que autentico el request, o null si entro por JWT.</summary>
    Guid? ApiKeyId { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
}
