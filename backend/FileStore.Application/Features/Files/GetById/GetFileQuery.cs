using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Application.Features.Files.Common;
using FileStore.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Files.GetById;

public record GetFileQuery(Guid Id) : IRequest<FileDto>;

public class GetFileQueryHandler(
    IApplicationDbContext context,
    ICurrentUser currentUser)
    : IRequestHandler<GetFileQuery, FileDto>
{
    public async Task<FileDto> Handle(GetFileQuery request, CancellationToken cancellationToken)
    {
        var clientId = currentUser.ClientId ?? throw new InvalidCredentialsException();

        return await context.Files
            .AsNoTracking()
            .Where(f => f.Id == request.Id && f.ClientId == clientId)
            .Select(f => new FileDto(
                f.Id,
                f.FolderId,
                f.OriginalName,
                f.SizeBytes,
                f.MimeType,
                f.Extension,
                f.Versions.Count,
                f.IsDeleted,
                f.DeletedAt,
                f.CreatedAt,
                f.UpdatedAt))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(StoredFile), request.Id);
    }
}
