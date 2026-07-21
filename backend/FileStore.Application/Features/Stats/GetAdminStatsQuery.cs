using FileStore.Application.Abstractions;
using FileStore.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Stats;

public record TopClientDto(Guid Id, string Email, string Name, long UsedBytes, long QuotaBytes);

public record AdminStatsDto(
    int TotalClients,
    int ActiveClients,
    int BlockedClients,
    long TotalUsedBytes,
    long TotalQuotaBytes,
    int TotalFiles,
    int FilesInTrash,
    IReadOnlyList<TopClientDto> TopClients,
    IReadOnlyList<DailyActivityDto> Daily);

public record GetAdminStatsQuery(int Days = 30) : IRequest<AdminStatsDto>;

public class GetAdminStatsQueryHandler(IApplicationDbContext context)
    : IRequestHandler<GetAdminStatsQuery, AdminStatsDto>
{
    public async Task<AdminStatsDto> Handle(
        GetAdminStatsQuery request,
        CancellationToken cancellationToken)
    {
        // Las cuentas dadas de baja se excluyen de todas las metricas: siguen en
        // la base por trazabilidad, pero no representan uso real del servicio.
        var clients = context.Clients.AsNoTracking().Where(c => !c.IsDeleted);

        var totals = await clients
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Active = g.Count(c => c.IsActive),
                UsedBytes = g.Sum(c => c.UsedBytes),
                QuotaBytes = g.Sum(c => c.QuotaBytes)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var topClients = await clients
            .OrderByDescending(c => c.UsedBytes)
            .Take(5)
            .Select(c => new TopClientDto(c.Id, c.Email, c.Name, c.UsedBytes, c.QuotaBytes))
            .ToListAsync(cancellationToken);

        var totalFiles = await context.Files.CountAsync(f => !f.IsDeleted, cancellationToken);
        var inTrash = await context.Files.CountAsync(f => f.IsDeleted, cancellationToken);

        var since = DateTime.UtcNow.Date.AddDays(-(request.Days - 1));

        var grouped = await context.AuditLogs
            .AsNoTracking()
            .Where(a => a.CreatedAt >= since
                && (a.Action == AuditAction.Upload || a.Action == AuditAction.Download))
            .GroupBy(a => new { Day = a.CreatedAt.Date, a.Action })
            .Select(g => new { g.Key.Day, g.Key.Action, Count = g.Count() })
            .ToListAsync(cancellationToken);

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

        return new AdminStatsDto(
            totals?.Total ?? 0,
            totals?.Active ?? 0,
            (totals?.Total ?? 0) - (totals?.Active ?? 0),
            totals?.UsedBytes ?? 0,
            totals?.QuotaBytes ?? 0,
            totalFiles,
            inTrash,
            topClients,
            daily);
    }
}
