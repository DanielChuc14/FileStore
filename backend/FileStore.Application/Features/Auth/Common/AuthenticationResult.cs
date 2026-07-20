namespace FileStore.Application.Features.Auth.Common;

/// <summary>
/// RefreshToken viaja en claro hasta el controller, que lo pone en la cookie
/// httpOnly. Nunca se serializa al body de la respuesta.
/// </summary>
public record AuthenticationResult(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    Guid UserId,
    string Email,
    string Role);
