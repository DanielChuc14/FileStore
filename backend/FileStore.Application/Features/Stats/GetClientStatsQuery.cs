using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Stats;

public record DailyActivityDto(DateOnly Date, int Uploads, int Downloads);

public record ClientStatsDto(IReadOnlyList<DailyActivityDto> Daily);

public record GetClientStatsQuery(int Days = 30) : IRequest<ClientStatsDto>;

public class GetClientStatsQueryValidator : AbstractValidator<GetClientStatsQuery>
{
    public GetClientStatsQueryValidator()
    {
        RuleFor(q => q.Days).InclusiveBetween(1, 365);
    }
}

public class GetClientStatsQueryHandler(
    IApplicationDbContext context,
    ICurrentUser currentUser)
    : IRequestHandler<GetClientStatsQuery, ClientStatsDto>
{
    public async Task<ClientStatsDto> Handle(
        GetClientStatsQuery request,
        CancellationToken cancellationToken)
    {
        var clientId = currentUser.ClientId ?? throw new InvalidCredentialsException();

        var since = DateTime.UtcNow.Date.AddDays(-(request.Days - 1));

        var grouped = await context.AuditLogs
            .AsNoTracking()
            .Where(a => a.ClientId == clientId
                && a.CreatedAt >= since
                && (a.Action == AuditAction.Upload || a.Action == AuditAction.Download))
            .GroupBy(a => new { Day = a.CreatedAt.Date, a.Action })
            .Select(g => new { g.Key.Day, g.Key.Action, Count = g.Count() })
            .ToListAsync(cancellationToken);

        // La serie se rellena con todos los dias del rango, incluidos los que no
        // tuvieron actividad: una grafica con huecos se lee mal, y el frontend no
        // deberia tener que inferir los ceros.
        var daily = Enumerable.Range(0, request.Days)
            .Select(offset =>
            {
                var day = since.AddDays(offset);

                return new DailyActivityDto(
                    DateOnly.FromDateTime(day),
                    grouped.FirstOrDefault(g => g.Day == day && g.Action == AuditAction.Upload)?.Count ?? 0,
                    grouped.FirstOrDefault(g => g.Day == day && g.Action == AuditAction.Download)?.Count ?? 0);
            })
            .ToList();

        return new ClientStatsDto(daily);
    }
}
