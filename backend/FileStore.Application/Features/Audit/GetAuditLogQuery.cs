using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Application.Common.Models;
using FileStore.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Audit;

public record AuditEntryDto(
    Guid Id,
    string Action,
    string ActorType,
    Guid ActorId,
    string? ResourceType,
    Guid? ResourceId,
    string? MetadataJson,
    string? IpAddress,
    DateTime CreatedAt);

public record GetAuditLogQuery(
    AuditAction? Action,
    string? ResourceType,
    DateTime? From,
    DateTime? To,
    int Page = 1,
    int PageSize = 50) : IRequest<PagedResult<AuditEntryDto>>;

public class GetAuditLogQueryValidator : AbstractValidator<GetAuditLogQuery>
{
    public GetAuditLogQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThan(0);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 200);
        RuleFor(q => q.To)
            .GreaterThanOrEqualTo(q => q.From!.Value)
            .When(q => q.From.HasValue && q.To.HasValue)
            .WithMessage("El rango de fechas es invalido: 'to' no puede ser anterior a 'from'.");
    }
}

public class GetAuditLogQueryHandler(
    IApplicationDbContext context,
    ICurrentUser currentUser)
    : IRequestHandler<GetAuditLogQuery, PagedResult<AuditEntryDto>>
{
    public async Task<PagedResult<AuditEntryDto>> Handle(
        GetAuditLogQuery request,
        CancellationToken cancellationToken)
    {
        var clientId = currentUser.ClientId ?? throw new InvalidCredentialsException();

        // El filtro por ClientId coincide con el indice (ClientId, CreatedAt),
        // que es el que sostiene esta consulta paginada por fecha.
        var query = context.AuditLogs
            .AsNoTracking()
            .Where(a => a.ClientId == clientId);

        if (request.Action.HasValue)
        {
            query = query.Where(a => a.Action == request.Action.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.ResourceType))
        {
            query = query.Where(a => a.ResourceType == request.ResourceType);
        }

        if (request.From.HasValue)
        {
            query = query.Where(a => a.CreatedAt >= request.From.Value);
        }

        if (request.To.HasValue)
        {
            query = query.Where(a => a.CreatedAt <= request.To.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(a => new AuditEntryDto(
                a.Id,
                a.Action.ToString(),
                a.ActorType.ToString(),
                a.ActorId,
                a.ResourceType,
                a.ResourceId,
                a.MetadataJson,
                a.IpAddress,
                a.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditEntryDto>(items, request.Page, request.PageSize, totalCount);
    }
}
