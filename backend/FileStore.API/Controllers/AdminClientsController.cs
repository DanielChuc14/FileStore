using FileStore.API.Contracts;
using FileStore.Application.Common;
using FileStore.Application.Common.Models;
using FileStore.Application.Features.Clients.Common;
using FileStore.Application.Features.Clients.Create;
using FileStore.Application.Features.Clients.Delete;
using FileStore.Application.Features.Clients.GetById;
using FileStore.Application.Features.Clients.GetList;
using FileStore.Application.Features.Clients.ResetPassword;
using FileStore.Application.Features.Clients.Update;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FileStore.API.Controllers;

[ApiController]
[Route("admin/clients")]
[Authorize(Policy = AuthPolicies.SuperAdmin)]
public class AdminClientsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<ClientDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ClientDto>>> GetAll(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(new GetClientsQuery(search, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ClientDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClientDto>> GetById(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetClientQuery(id), cancellationToken));

    /// <summary>
    /// Da de alta un cliente. La contraseña generada NO vuelve en la respuesta:
    /// se le envia por correo directamente al cliente.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<ClientDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClientDto>> Create(
        CreateClientRequest request,
        CancellationToken cancellationToken)
    {
        var client = await sender.Send(
            new CreateClientCommand(
                request.Email,
                request.Name,
                request.QuotaBytes,
                request.TrashRetentionDays,
                request.MaxFileSizeBytes),
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = client.Id }, client);
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType<ClientDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClientDto>> Update(
        Guid id,
        UpdateClientRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new UpdateClientCommand(
                id,
                request.Name,
                request.QuotaBytes,
                request.IsActive,
                request.TrashRetentionDays,
                request.MaxFileSizeBytes,
                request.ClearTrashRetentionOverride,
                request.ClearMaxFileSizeOverride),
            cancellationToken));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteClientCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Restablece la contraseña del cliente y se la envia por correo. La
    /// respuesta no la incluye: el super-admin dispara el reseteo, no lo recibe.
    /// </summary>
    [HttpPost("{id:guid}/reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword(
        Guid id,
        CancellationToken cancellationToken)
    {
        await sender.Send(new ResetClientPasswordCommand(id), cancellationToken);
        return NoContent();
    }
}
