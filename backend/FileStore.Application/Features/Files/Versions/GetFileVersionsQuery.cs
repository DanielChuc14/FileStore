using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Files.Versions;

public record FileVersionDto(
    Guid Id,
    int VersionNumber,
    long SizeBytes,
    string MimeType,
    string ChecksumSha256,
    bool IsCurrent,
    DateTime CreatedAt);

public record GetFileVersionsQuery(Guid FileId) : IRequest<IReadOnlyList<FileVersionDto>>;

public class GetFileVersionsQueryHandler(
    IApplicationDbContext context,
    ICurrentUser currentUser)
    : IRequestHandler<GetFileVersionsQuery, IReadOnlyList<FileVersionDto>>
{
    public async Task<IReadOnlyList<FileVersionDto>> Handle(
        GetFileVersionsQuery request,
        CancellationToken cancellationToken)
    {
        var clientId = currentUser.ClientId ?? throw new InvalidCredentialsException();

        // La propiedad se verifica sobre el archivo, no sobre las versiones: si
        // el archivo no es del cliente, no se filtra ni la existencia.
        var file = await context.Files
            .Where(f => f.Id == request.FileId && f.ClientId == clientId)
            .Select(f => new { f.Id, f.CurrentVersionId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(StoredFile), request.FileId);

        return await context.FileVersions
            .AsNoTracking()
            .Where(v => v.FileId == file.Id)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new FileVersionDto(
                v.Id,
                v.VersionNumber,
                v.SizeBytes,
                v.MimeType,
                v.ChecksumSha256,
                v.Id == file.CurrentVersionId,
                v.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
