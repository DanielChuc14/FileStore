using FileStore.Domain.Enums;

namespace FileStore.Application.Abstractions;

/// <summary>
/// Quien esta ejecutando el request. Application no puede leer HttpContext
/// (no conoce ASP.NET), asi que la capa API lo implementa a partir de los claims.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    UserType? UserType { get; }
    string? Email { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
}
