using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Application.Common.Models;
using FileStore.Application.Features.Files.Common;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Files.GetList;

public record GetFilesQuery(
    Guid? FolderId,
    string? Name,
    bool Deleted = false,
    int Page = 1,
    int PageSize = 50) : IRequest<PagedResult<FileDto>>;

public class GetFilesQueryValidator : AbstractValidator<GetFilesQuery>
{
    public GetFilesQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThan(0);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 200);
    }
}

public class GetFilesQueryHandler(
    IApplicationDbContext context,
    ICurrentUser currentUser)
    : IRequestHandler<GetFilesQuery, PagedResult<FileDto>>
{
    public async Task<PagedResult<FileDto>> Handle(
        GetFilesQuery request,
        CancellationToken cancellationToken)
    {
        var clientId = currentUser.ClientId ?? throw new InvalidCredentialsException();

        // ClientId primero en el Where: es el filtro que garantiza el aislamiento
        // y coincide con el indice (ClientId, FolderId, IsDeleted).
        var query = context.Files
            .AsNoTracking()
            .Where(f => f.ClientId == clientId && f.IsDeleted == request.Deleted);

        // En la papelera se listan todos los archivos borrados del cliente, sin
        // importar de que carpeta venian (la carpeta pudo haberse eliminado).
        if (!request.Deleted)
        {
            query = query.Where(f => f.FolderId == request.FolderId);
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var name = request.Name.Trim().ToLowerInvariant();
            query = query.Where(f => f.OriginalName.ToLower().Contains(name));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(f => f.UpdatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
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
            .ToListAsync(cancellationToken);

        return new PagedResult<FileDto>(items, request.Page, request.PageSize, totalCount);
    }
}
