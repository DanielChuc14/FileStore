using FileStore.Domain.Enums;

namespace FileStore.Application.Abstractions;

public interface IJwtTokenGenerator
{
    /// <summary>Emite el access token firmado y devuelve su vencimiento.</summary>
    (string Token, DateTime ExpiresAt) GenerateAccessToken(Guid userId, string email, UserType userType);

    /// <summary>
    /// Genera un refresh token aleatorio. Devuelve el valor en claro (que viaja
    /// al cliente en la cookie y no se persiste) y su hash (que si se persiste).
    /// </summary>
    (string Token, string TokenHash, DateTime ExpiresAt) GenerateRefreshToken();

    /// <summary>Hashea un refresh token recibido para poder buscarlo en la base.</summary>
    string HashRefreshToken(string token);
}
