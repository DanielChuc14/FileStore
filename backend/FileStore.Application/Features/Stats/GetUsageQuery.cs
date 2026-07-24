using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Stats;

public record UsageDto(
    long UsedBytes,
    long QuotaBytes,
    int FilesCount,
    int TrashCount,
    long TrashBytes,
    double UsedPercentage);

public record GetUsageQuery : IRequest<UsageDto>;

public class GetUsageQueryHandler(
    IApplicationDbContext context,
    ICurrentUser currentUser)
    : IRequestHandler<GetUsageQuery, UsageDto>
{
    public async Task<UsageDto> Handle(GetUsageQuery request, CancellationToken cancellationToken)
    {
        var clientId = currentUser.ClientId ?? throw new InvalidCredentialsException();

        var client = await context.Clients
            .AsNoTracking()
            .Where(c => c.Id == clientId)
            .Select(c => new { c.UsedBytes, c.QuotaBytes })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidCredentialsException();

        var filesCount = await context.Files
            .CountAsync(f => f.ClientId == clientId && !f.IsDeleted, cancellationToken);

        // El peso de la papelera se calcula sumando las versiones y no el
        // SizeBytes del archivo: ese refleja solo la version vigente, mientras
        // que la cuota la ocupan todas.
        var trashStats = await context.Files
            .AsNoTracking()
            .Where(f => f.ClientId == clientId && f.IsDeleted)
            .Select(f => new { Count = 1, Bytes = f.Versions.Sum(v => v.SizeBytes) })
            .ToListAsync(cancellationToken);

        var quota = client.QuotaBytes;
        var percentage = quota == 0 ? 0 : Math.Round(client.UsedBytes * 100.0 / quota, 2);

        return new UsageDto(
            client.UsedBytes,
            quota,
            filesCount,
            trashStats.Count,
            trashStats.Sum(t => t.Bytes),
            percentage);
    }
}
