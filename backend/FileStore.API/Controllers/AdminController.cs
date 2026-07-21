using System.Security.Claims;
using FileStore.API.Contracts;
using FileStore.Application.Common;
using FileStore.Application.Features.Config;
using FileStore.Application.Features.Stats;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FileStore.API.Controllers;

[ApiController]
[Route("admin")]
[Authorize(Policy = AuthPolicies.SuperAdmin)]
public class AdminController(ISender sender) : ControllerBase
{
    [HttpGet("whoami")]
    public IActionResult WhoAmI() => Ok(new
    {
        UserId = User.FindFirstValue(AuthClaims.UserId),
        Email = User.FindFirstValue(AuthClaims.Email),
        Role = User.FindFirstValue(AuthClaims.Role)
    });

    [HttpGet("stats")]
    [ProducesResponseType<AdminStatsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminStatsDto>> GetStats(
        [FromQuery] int days = 30,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(new GetAdminStatsQuery(days), cancellationToken));

    [HttpGet("config")]
    [ProducesResponseType<AppConfigDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AppConfigDto>> GetConfig(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetConfigQuery(), cancellationToken));

    [HttpPatch("config")]
    [ProducesResponseType<AppConfigDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AppConfigDto>> UpdateConfig(
        UpdateConfigRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new UpdateConfigCommand(
                request.MaxFileSizeBytes,
                request.TrashRetentionDays,
                request.RateLimitDefaultPerMinute),
            cancellationToken));

    [HttpGet("allowed-types")]
    [ProducesResponseType<IReadOnlyList<AllowedTypeDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AllowedTypeDto>>> GetAllowedTypes(
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetAllowedTypesQuery(), cancellationToken));

    [HttpPatch("allowed-types/{id:guid}")]
    [ProducesResponseType<AllowedTypeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AllowedTypeDto>> UpdateAllowedType(
        Guid id,
        UpdateAllowedTypeRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new UpdateAllowedTypeCommand(id, request.IsEnabled), cancellationToken));
}
