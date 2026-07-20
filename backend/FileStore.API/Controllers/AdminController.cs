using System.Security.Claims;
using FileStore.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FileStore.API.Controllers;

/// <summary>
/// Endpoint minimo para verificar que la autorizacion por rol funciona.
/// La gestion de clientes se implementa en la Fase 3.
/// </summary>
[ApiController]
[Route("admin")]
[Authorize(Policy = AuthPolicies.SuperAdmin)]
public class AdminController : ControllerBase
{
    [HttpGet("whoami")]
    public IActionResult WhoAmI() => Ok(new
    {
        UserId = User.FindFirstValue(AuthClaims.UserId),
        Email = User.FindFirstValue(AuthClaims.Email),
        Role = User.FindFirstValue(AuthClaims.Role)
    });
}
