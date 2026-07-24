using FileStore.API.Contracts;
using FileStore.Application.Common.Exceptions;
using FileStore.Application.Features.Auth.Common;
using FileStore.Application.Features.Auth.Login;
using FileStore.Application.Features.Auth.Logout;
using FileStore.Application.Features.Auth.Refresh;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FileStore.API.Controllers;

[ApiController]
[Route("auth")]
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

        Response.Cookies.Delete(RefreshCookieName, new CookieOptions
        {
            Path = RefreshCookiePath,
            Secure = true,
            HttpOnly = true,
            SameSite = SameSiteMode.Strict
        });

        return NoContent();
    }

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
