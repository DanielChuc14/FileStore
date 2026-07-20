using System.Security.Claims;
using FileStore.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FileStore.API.Controllers;

/// <summary>
/// Endpoint minimo para verificar que la autorizacion por rol funciona.
/// El perfil completo se implementa en la Fase 8.
/// </summary>
[ApiController]
[Route("me")]
[Authorize(Policy = AuthPolicies.Client)]
public class MeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        UserId = User.FindFirstValue(AuthClaims.UserId),
        Email = User.FindFirstValue(AuthClaims.Email),
        Role = User.FindFirstValue(AuthClaims.Role)
    });
}
