using FileStore.API.Contracts;
using FileStore.API.Infrastructure;
using FileStore.Application.Common;
using FileStore.Application.Features.Folders.Common;
using FileStore.Application.Features.Folders.Create;
using FileStore.Application.Features.Folders.Delete;
using FileStore.Application.Features.Folders.GetList;
using FileStore.Application.Features.Folders.Update;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FileStore.API.Controllers;

[ApiController]
[Route(ApiRoutes.V1 + "/folders")]
[Authorize(Policy = AuthPolicies.ClientContent)]
public class FoldersController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<FolderDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<FolderDto>>> GetAll(
        [FromQuery] Guid? parentId,
        [FromQuery] bool all = false,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(new GetFoldersQuery(parentId, all), cancellationToken));

    [HttpPost]
    [ProducesResponseType<FolderDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FolderDto>> Create(
        CreateFolderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new CreateFolderCommand(request.Name, request.ParentId),
            cancellationToken);

        return CreatedAtAction(nameof(GetAll), new { parentId = result.ParentFolderId }, result);
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType<FolderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FolderDto>> Update(
        Guid id,
        UpdateFolderRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new UpdateFolderCommand(id, request.Name, request.ParentId, request.MoveToRoot),
            cancellationToken));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromQuery] bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        await sender.Send(new DeleteFolderCommand(id, recursive), cancellationToken);
        return NoContent();
    }
}
