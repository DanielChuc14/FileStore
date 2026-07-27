using FileStore.API.Infrastructure;
using System.Security.Claims;
using FileStore.Application.Common;
using FileStore.Application.Common.Models;
using FileStore.API.Contracts;
using FileStore.Application.Features.Audit;
using FileStore.Application.Features.Profile;
using FileStore.Application.Features.Stats;
using FileStore.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FileStore.API.Controllers;

[ApiController]
[Route(ApiRoutes.V1 + "/me")]
[Authorize(Policy = AuthPolicies.Client)]
public class MeController(ISender sender) : ControllerBase
{
    /// <summary>Cookie con el refresh token de la sesion en curso.</summary>
    private const string RefreshCookieName = "fs_refresh";

    [HttpGet]
    [ProducesResponseType<ProfileDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProfileDto>> Get(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetProfileQuery(), cancellationToken));

    [HttpPost("change-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        // El refresh token de la sesion actual se pasa al handler para que la
        // conserve viva y revoque unicamente las demas.
        await sender.Send(
            new ChangePasswordCommand(
                request.CurrentPassword,
                request.NewPassword,
                Request.Cookies[RefreshCookieName]),
            cancellationToken);

        return NoContent();
    }

    [HttpGet("usage")]
    [ProducesResponseType<UsageDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UsageDto>> GetUsage(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetUsageQuery(), cancellationToken));

    [HttpGet("stats")]
    [ProducesResponseType<ClientStatsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ClientStatsDto>> GetStats(
        [FromQuery] int days = 30,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(new GetClientStatsQuery(days), cancellationToken));

    [HttpGet("audit-log")]
    [ProducesResponseType<PagedResult<AuditEntryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AuditEntryDto>>> GetAuditLog(
        [FromQuery] AuditAction? action,
        [FromQuery] string? resourceType,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(
            new GetAuditLogQuery(action, resourceType, from, to, page, pageSize),
            cancellationToken));
}
