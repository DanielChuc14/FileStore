using System.Security.Claims;
using FileStore.Application.Common;
using FileStore.Application.Common.Models;
using FileStore.Application.Features.Audit;
using FileStore.Application.Features.Stats;
using FileStore.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FileStore.API.Controllers;

[ApiController]
[Route("me")]
[Authorize(Policy = AuthPolicies.Client)]
public class MeController(ISender sender) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        UserId = User.FindFirstValue(AuthClaims.UserId),
        Email = User.FindFirstValue(AuthClaims.Email),
        Role = User.FindFirstValue(AuthClaims.Role)
    });

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
