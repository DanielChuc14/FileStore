namespace FileStore.API.Contracts;

public record LoginRequest(string Email, string Password);

/// <summary>
/// Respuesta del login y del refresh. No incluye el refresh token a proposito:
/// ese viaja solo en la cookie httpOnly, donde JavaScript no puede leerlo.
/// </summary>
public record AuthResponse(
    string AccessToken,
    DateTime ExpiresAt,
    Guid UserId,
    string Email,
    string Role);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record ForgotPasswordRequest(string Email);

/// <summary>Token del enlace del correo, mas la contraseña elegida.</summary>
public record ResetPasswordRequest(string Token, string NewPassword);
