using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Trash;

public record TrashItemDto(
    Guid Id,
    string OriginalName,
    long SizeBytes,
    string Extension,
    DateTime DeletedAt,
    DateTime PurgeAt,
    int DaysUntilPurge);

public record GetTrashQuery : IRequest<IReadOnlyList<TrashItemDto>>;

public class GetTrashQueryHandler(
    IApplicationDbContext context,
    IAppConfigReader appConfig,
    ICurrentUser currentUser)
    : IRequestHandler<GetTrashQuery, IReadOnlyList<TrashItemDto>>
{
    public async Task<IReadOnlyList<TrashItemDto>> Handle(
        GetTrashQuery request,
        CancellationToken cancellationToken)
    {
        var clientId = currentUser.ClientId ?? throw new InvalidCredentialsException();

        // La retencion del cliente manda; si no tiene override, se usa la global.
        var globalRetention = await appConfig.GetTrashRetentionDaysAsync(cancellationToken);

        var clientRetention = await context.Clients
            .Where(c => c.Id == clientId)
            .Select(c => c.TrashRetentionDays)
            .FirstOrDefaultAsync(cancellationToken);

        var retention = clientRetention ?? globalRetention;

        var items = await context.Files
            .AsNoTracking()
            .Where(f => f.ClientId == clientId && f.IsDeleted && f.DeletedAt != null)
            .OrderByDescending(f => f.DeletedAt)
            .Select(f => new
            {
                f.Id,
                f.OriginalName,
                f.SizeBytes,
                f.Extension,
                DeletedAt = f.DeletedAt!.Value
            })
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;

        // La fecha de purga se calcula en memoria: depende de la retencion
        // vigente, que puede cambiar despues de que el archivo fuera borrado.
        return items
            .Select(f =>
            {
                var purgeAt = f.DeletedAt.AddDays(retention);
                var daysLeft = (int)Math.Ceiling((purgeAt - now).TotalDays);

                return new TrashItemDto(
                    f.Id,
                    f.OriginalName,
                    f.SizeBytes,
                    f.Extension,
                    f.DeletedAt,
                    purgeAt,
                    Math.Max(daysLeft, 0));
            })
            .ToList();
    }
}
