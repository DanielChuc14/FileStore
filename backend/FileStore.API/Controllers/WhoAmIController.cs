using System.Security.Claims;
using FileStore.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FileStore.API.Controllers;

/// <summary>
/// Endpoint de diagnostico de la API publica: permite a quien integra verificar
/// que su API Key funciona y contra que cliente resuelve, sin tener que subir
/// un archivo para probarlo.
/// </summary>
[ApiController]
[Route("whoami")]
[Authorize(Policy = AuthPolicies.ApiKey)]
public class WhoAmIController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        ClientId = User.FindFirstValue(AuthClaims.ClientId),
        ClientName = User.FindFirstValue(ClaimTypes.Name),
        ApiKeyId = User.FindFirstValue(AuthClaims.ApiKeyId)
    });
}
