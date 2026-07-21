using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Files.Versions;

public record RestoreVersionCommand(Guid FileId, int VersionNumber) : IRequest;

/// <summary>
/// Restaurar reapunta CurrentVersionId a una version anterior. NO crea una
/// version nueva ni copia el binario: la version restaurada ya existe en disco
/// y ya esta contabilizada en la cuota. Por eso la cuota no cambia.
/// </summary>
public class RestoreVersionCommandHandler(
    IApplicationDbContext context,
    ICurrentUser currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<RestoreVersionCommand>
{
    public async Task Handle(RestoreVersionCommand request, CancellationToken cancellationToken)
    {
        var clientId = currentUser.ClientId ?? throw new InvalidCredentialsException();

        var file = await context.Files
            .FirstOrDefaultAsync(
                f => f.Id == request.FileId && f.ClientId == clientId && !f.IsDeleted,
                cancellationToken)
            ?? throw new NotFoundException(nameof(StoredFile), request.FileId);

        var version = await context.FileVersions
            .Where(v => v.FileId == file.Id && v.VersionNumber == request.VersionNumber)
            .Select(v => new { v.Id, v.SizeBytes, v.MimeType, v.VersionNumber })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("FileVersion", request.VersionNumber);

        if (file.CurrentVersionId == version.Id)
        {
            // Ya es la vigente: no hay nada que hacer.
            return;
        }

        var previousVersionId = file.CurrentVersionId;

        file.CurrentVersionId = version.Id;
        file.SizeBytes = version.SizeBytes;
        file.MimeType = version.MimeType;
        file.UpdatedAt = DateTime.UtcNow;

        auditLogger.Record(
            AuditAction.RestoreVersion,
            clientId: clientId,
            resourceType: nameof(StoredFile),
            resourceId: file.Id,
            metadata: new
            {
                file.OriginalName,
                RestoredVersion = version.VersionNumber,
                PreviousVersionId = previousVersionId
            });

        await context.SaveChangesAsync(cancellationToken);
    }
}
