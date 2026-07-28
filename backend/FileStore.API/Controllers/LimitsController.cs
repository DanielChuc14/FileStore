using FileStore.API.Infrastructure;
using FileStore.Application.Common;
using FileStore.Application.Features.Limits;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FileStore.API.Controllers;

[ApiController]
[Route(ApiRoutes.V1 + "/limits")]
// Acepta los dos canales a proposito. Es el endpoint que una integracion con API
// Key consulta al arrancar, y hasta ahora ese canal no tenia forma de conocer ni
// su cuota ni las extensiones permitidas.
[Authorize(Policy = AuthPolicies.ClientContent)]
public class LimitsController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Limites y cuota efectivos para la credencial que hace la llamada.
    ///
    /// Pensado para consultarse al arrancar la integracion y cachearse un rato:
    /// permite validar del lado del cliente antes de subir, en vez de descubrir
    /// los limites a base de 400 y 413. Como son configurables por despliegue,
    /// consultarlos es la unica forma de no quedarse con un valor obsoleto.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<LimitsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LimitsDto>> Get(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetLimitsQuery(), cancellationToken));
}
