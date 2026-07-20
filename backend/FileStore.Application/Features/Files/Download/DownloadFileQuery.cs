using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Application.Features.Files.Common;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Files.Download;

/// <summary>Version null descarga la vigente.</summary>
public record DownloadFileQuery(Guid FileId, int? Version) : IRequest<FileDownload>;

public class DownloadFileQueryHandler(
    IApplicationDbContext context,
    IStorageService storage,
    ICurrentUser currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<DownloadFileQuery, FileDownload>
{
    public async Task<FileDownload> Handle(
        DownloadFileQuery request,
        CancellationToken cancellationToken)
    {
        var clientId = currentUser.ClientId ?? throw new InvalidCredentialsException();

        // La propiedad se valida en el WHERE: pedir un archivo de otro cliente
        // devuelve 404, sin confirmar que existe.
        var file = await context.Files
            .Where(f => f.Id == request.FileId && f.ClientId == clientId)
            .Select(f => new
            {
                f.Id,
                f.OriginalName,
                f.CurrentVersionId,
                f.IsDeleted
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(StoredFile), request.FileId);

        if (file.IsDeleted)
        {
            throw new NotFoundException(nameof(StoredFile), request.FileId);
        }

        var versionQuery = context.FileVersions.Where(v => v.FileId == file.Id);

        versionQuery = request.Version.HasValue
            ? versionQuery.Where(v => v.VersionNumber == request.Version.Value)
            : versionQuery.Where(v => v.Id == file.CurrentVersionId);

        var version = await versionQuery
            .Select(v => new { v.StoragePath, v.MimeType, v.SizeBytes, v.VersionNumber })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("FileVersion", request.Version ?? 0);

        var content = await storage.OpenReadAsync(version.StoragePath, cancellationToken);

        auditLogger.Record(
            AuditAction.Download,
            clientId: clientId,
            resourceType: nameof(StoredFile),
            resourceId: file.Id,
            metadata: new { file.OriginalName, version.VersionNumber });

        await context.SaveChangesAsync(cancellationToken);

        return new FileDownload(content, file.OriginalName, version.MimeType, version.SizeBytes);
    }
}
