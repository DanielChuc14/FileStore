using FileStore.API.Contracts;
using FileStore.Application.Common;
using FileStore.Application.Features.ApiKeys.Common;
using FileStore.Application.Features.ApiKeys.Create;
using FileStore.Application.Features.ApiKeys.GetList;
using FileStore.Application.Features.ApiKeys.Revoke;
using FileStore.Application.Features.ApiKeys.Rotate;
using FileStore.Application.Features.ApiKeys.Update;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FileStore.API.Controllers;

/// <summary>
/// Autogestion de API Keys por parte del cliente. Va bajo JWT y no bajo API Key:
/// una key no deberia poder crear otras keys ni revocarse a si misma.
/// </summary>
[ApiController]
[Route("me/api-keys")]
[Authorize(Policy = AuthPolicies.Client)]
public class MeApiKeysController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ApiKeyDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ApiKeyDto>>> GetAll(
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetApiKeysQuery(), cancellationToken));

    [HttpPost]
    [ProducesResponseType<CreateApiKeyResult>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateApiKeyResult>> Create(
        CreateApiKeyRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new CreateApiKeyCommand(request.Name, request.RateLimitPerMinute),
            cancellationToken);

        return CreatedAtAction(nameof(GetAll), null, result);
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType<ApiKeyDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiKeyDto>> Update(
        Guid id,
        UpdateApiKeyRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new UpdateApiKeyCommand(
                id,
                request.Name,
                request.RateLimitPerMinute,
                request.ClearRateLimitOverride),
            cancellationToken));

    [HttpPost("{id:guid}/rotate")]
    [ProducesResponseType<CreateApiKeyResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CreateApiKeyResult>> Rotate(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new RotateApiKeyCommand(id), cancellationToken));

    [HttpPost("{id:guid}/revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new RevokeApiKeyCommand(id), cancellationToken);
        return NoContent();
    }
}
