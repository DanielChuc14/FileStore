using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Application.Features.Folders.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Folders.GetList;

/// <summary>Si ParentId es null devuelve la raiz. All=true devuelve el arbol completo.</summary>
public record GetFoldersQuery(Guid? ParentId, bool All = false)
    : IRequest<IReadOnlyList<FolderDto>>;

public class GetFoldersQueryHandler(
    IApplicationDbContext context,
    ICurrentUser currentUser)
    : IRequestHandler<GetFoldersQuery, IReadOnlyList<FolderDto>>
{
    public async Task<IReadOnlyList<FolderDto>> Handle(
        GetFoldersQuery request,
        CancellationToken cancellationToken)
    {
        var clientId = currentUser.ClientId ?? throw new InvalidCredentialsException();

        var query = context.Folders
            .AsNoTracking()
            .Where(f => f.ClientId == clientId);

        if (!request.All)
        {
            query = query.Where(f => f.ParentFolderId == request.ParentId);
        }

        return await query
            .OrderBy(f => f.Name)
            .Select(f => new FolderDto(
                f.Id,
                f.ParentFolderId,
                f.Name,
                f.Path,
                f.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
