using FileStore.API.Contracts;
using FileStore.API.Infrastructure;
using FileStore.Application.Common;
using FileStore.Application.Common.Models;
using FileStore.Application.Features.Files.Common;
using FileStore.Application.Features.Files.Delete;
using FileStore.Application.Features.Files.Download;
using FileStore.Application.Features.Files.GetById;
using FileStore.Application.Features.Files.GetList;
using FileStore.Application.Features.Files.Update;
using FileStore.Application.Features.Files.Upload;
using FileStore.Application.Features.Files.Versions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FileStore.API.Controllers;

[ApiController]
[Route(ApiRoutes.V1 + "/files")]
[Authorize(Policy = AuthPolicies.ClientContent)]
public class FilesController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<FileDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<FileDto>>> GetAll(
        [FromQuery] Guid? folderId,
        [FromQuery] string? name,
        [FromQuery] bool deleted = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(
            new GetFilesQuery(folderId, name, deleted, page, pageSize),
            cancellationToken));

    [HttpPost]
    [ProducesResponseType<FileDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<FileDto>> Upload(
        IFormFile file,
        [FromQuery] Guid? folderId,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Se requiere un archivo con contenido."
            });
        }

        // OpenReadStream no carga el archivo en memoria: se va copiando al disco
        // a medida que llega.
        await using var content = file.OpenReadStream();

        var result = await sender.Send(
            new UploadFileCommand(file.FileName, file.Length, content, folderId),
            cancellationToken);

        return CreatedAtAction(nameof(GetMetadata), new { id = result.Id }, result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(
        Guid id,
        [FromQuery] int? version,
        CancellationToken cancellationToken)
    {
        var download = await sender.Send(new DownloadFileQuery(id, version), cancellationToken);

        // File() con un Stream lo envia por streaming, sin bufferizarlo entero.
        return File(
            download.Content,
            download.MimeType,
            download.FileName,
            enableRangeProcessing: true);
    }

    [HttpGet("{id:guid}/metadata")]
    [ProducesResponseType<FileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FileDto>> GetMetadata(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetFileQuery(id), cancellationToken));

    [HttpPatch("{id:guid}")]
    [ProducesResponseType<FileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FileDto>> Update(
        Guid id,
        UpdateFileRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new UpdateFileCommand(id, request.Name, request.FolderId, request.MoveToRoot),
            cancellationToken));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteFileCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/versions")]
    [ProducesResponseType<IReadOnlyList<FileVersionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<FileVersionDto>>> GetVersions(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetFileVersionsQuery(id), cancellationToken));

    /// <summary>Reapunta la version vigente. No crea una version nueva.</summary>
    [HttpPost("{id:guid}/versions/{versionNumber:int}/restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RestoreVersion(
        Guid id,
        int versionNumber,
        CancellationToken cancellationToken)
    {
        await sender.Send(new RestoreVersionCommand(id, versionNumber), cancellationToken);
        return NoContent();
    }
}
