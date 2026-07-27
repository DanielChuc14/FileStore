using FileStore.API.Infrastructure;
using FileStore.Application.Common;
using FileStore.Application.Features.Trash;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FileStore.API.Controllers;

[ApiController]
[Route(ApiRoutes.V1 + "/trash")]
[Authorize(Policy = AuthPolicies.ClientContent)]
public class TrashController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TrashItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TrashItemDto>>> GetAll(
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetTrashQuery(), cancellationToken));

    [HttpPost("{id:guid}/restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new RestoreFromTrashCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>Borrado irreversible: elimina el binario y libera la cuota.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> HardDelete(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new HardDeleteCommand(id), cancellationToken);
        return NoContent();
    }
}
