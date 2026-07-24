using FileStore.Application.Abstractions;
using FileStore.Application.Common.Models;
using FileStore.Application.Features.Clients.Common;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Clients.GetList;

public record GetClientsQuery(string? Search, int Page = 1, int PageSize = 20)
    : IRequest<PagedResult<ClientDto>>;

public class GetClientsQueryValidator : AbstractValidator<GetClientsQuery>
{
    public GetClientsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThan(0);

        // Tope para que nadie pida 100000 registros de una y tumbe la respuesta.
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
    }
}

public class GetClientsQueryHandler(IApplicationDbContext context)
    : IRequestHandler<GetClientsQuery, PagedResult<ClientDto>>
{
    public async Task<PagedResult<ClientDto>> Handle(
        GetClientsQuery request,
        CancellationToken cancellationToken)
    {
        // AsNoTracking: es solo lectura, no hace falta que EF vigile cambios.
        var query = context.Clients.AsNoTracking().Where(c => !c.IsDeleted);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(c =>
                c.Email.Contains(search) || c.Name.ToLower().Contains(search));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // La proyeccion va escrita a mano y no con ClientDto.From: EF no sabe
        // traducir una llamada a metodo estatico a SQL. Escrita asi, solo trae
        // las columnas necesarias.
        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(c => new ClientDto(
                c.Id,
                c.Email,
                c.Name,
                c.QuotaBytes,
                c.UsedBytes,
                c.IsActive,
                c.TrashRetentionDays,
                c.MaxFileSizeBytes,
                c.CreatedAt,
                c.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<ClientDto>(items, request.Page, request.PageSize, totalCount);
    }
}
