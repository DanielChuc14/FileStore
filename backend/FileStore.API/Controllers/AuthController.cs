using FileStore.API.Contracts;
using FileStore.API.Infrastructure;
using FileStore.Application.Common.Exceptions;
using FileStore.Application.Features.Auth.Common;
using FileStore.Application.Features.Auth.ForgotPassword;
using FileStore.Application.Features.Auth.Login;
using FileStore.Application.Features.Auth.Logout;
using FileStore.Application.Features.Auth.Refresh;
using FileStore.Application.Features.Auth.ResetPassword;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FileStore.API.Controllers;

[ApiController]
[Route(ApiRoutes.V1 + "/auth")]
// Limite por IP: frena la fuerza bruta contra el login sin afectar el limite por
// API Key del resto de la API.
[EnableRateLimiting("auth")]
public class AuthController(ISender sender) : ControllerBase
{
    private const string RefreshCookieName = "fs_refresh";

    /// <summary>
    /// Path en la raiz para que la cookie funcione igual sin importar bajo que
    /// prefijo se monte la API (en desarrollo el panel la consume via /api).
    /// Acotarla a /auth ahorraria unos bytes por request, pero el path no es una
    /// barrera de seguridad: quien protege la cookie son HttpOnly y SameSite.
    /// </summary>
    private const string RefreshCookiePath = "/";

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new LoginCommand(request.Email, request.Password, GetIpAddress()),
            cancellationToken);

        SetRefreshCookie(result);

        return Ok(ToResponse(result));
    }

    /// <summary>
    /// Pide un enlace de recuperacion por correo.
    ///
    /// Responde 204 SIEMPRE, exista o no la cuenta. Devolver 404 para un email
    /// desconocido convertiria este endpoint en un enumerador de clientes
    /// registrados, que es justo lo que el login evita con su hash de descarte.
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ForgotPassword(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new ForgotPasswordCommand(request.Email, GetIpAddress()),
            cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Canjea el token del correo por una contraseña nueva. Cierra todas las
    /// sesiones abiertas del cliente.
    /// </summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new ResetPasswordCommand(request.Token, request.NewPassword),
            cancellationToken);

        // La cookie de refresh que pudiera quedar en el navegador ya no sirve:
        // el canje revoco todas las sesiones. Se borra para que el panel no
        // intente renovar con una credencial muerta.
        ClearRefreshCookie();

        return NoContent();
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken cancellationToken)
    {
        var token = Request.Cookies[RefreshCookieName];

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidCredentialsException();
        }

        var result = await sender.Send(
            new RefreshTokenCommand(token, GetIpAddress()),
            cancellationToken);

        SetRefreshCookie(result);

        return Ok(ToResponse(result));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await sender.Send(
            new LogoutCommand(Request.Cookies[RefreshCookieName]),
            cancellationToken);

        ClearRefreshCookie();

        return NoContent();
    }

    /// <summary>
    /// Borra la cookie de refresh. Las opciones tienen que coincidir con las de
    /// SetRefreshCookie o el navegador no la reconoce como la misma y no la borra.
    /// </summary>
    private void ClearRefreshCookie() =>
        Response.Cookies.Delete(RefreshCookieName, new CookieOptions
        {
            Path = RefreshCookiePath,
            Secure = true,
            HttpOnly = true,
            SameSite = SameSiteMode.Strict
        });

    private void SetRefreshCookie(AuthenticationResult result)
    {
        Response.Cookies.Append(RefreshCookieName, result.RefreshToken, new CookieOptions
        {
            // Inaccesible desde JavaScript: un XSS no puede robar la sesion.
            HttpOnly = true,

            // Solo por HTTPS.
            Secure = true,

            // No se envia en requests originados por otros sitios: cierra CSRF
            // sobre /auth/refresh.
            SameSite = SameSiteMode.Strict,

            Path = RefreshCookiePath,
            Expires = result.RefreshTokenExpiresAt
        });
    }

    private static AuthResponse ToResponse(AuthenticationResult result) => new(
        result.AccessToken,
        result.AccessTokenExpiresAt,
        result.UserId,
        result.Email,
        result.Role);

    private string? GetIpAddress() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
