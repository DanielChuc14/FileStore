using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Trash;

public record RestoreFromTrashCommand(Guid Id) : IRequest;

public class RestoreFromTrashCommandHandler(
    IApplicationDbContext context,
    ICurrentUser currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<RestoreFromTrashCommand>
{
    public async Task Handle(RestoreFromTrashCommand request, CancellationToken cancellationToken)
    {
        var clientId = currentUser.ClientId ?? throw new InvalidCredentialsException();

        var file = await context.Files
            .FirstOrDefaultAsync(
                f => f.Id == request.Id && f.ClientId == clientId && f.IsDeleted,
                cancellationToken)
            ?? throw new NotFoundException(nameof(StoredFile), request.Id);

        // Si la carpeta original se elimino, FolderId quedo en null al borrarla:
        // el archivo se restaura en la raiz en lugar de fallar.
        var duplicate = await context.Files.AnyAsync(
            f => f.ClientId == clientId
                && f.FolderId == file.FolderId
                && f.OriginalName == file.OriginalName
                && f.Id != file.Id
                && !f.IsDeleted,
            cancellationToken);

        if (duplicate)
        {
            throw new ConflictException(
                $"Ya existe un archivo llamado '{file.OriginalName}' en el destino. " +
                "Renombra o mueve el existente antes de restaurar.");
        }

        file.IsDeleted = false;
        file.DeletedAt = null;
        file.UpdatedAt = DateTime.UtcNow;

        // La cuota no cambia: el archivo la seguia ocupando mientras estaba en
        // la papelera (decision 1 de la seccion 15).
        auditLogger.Record(
            AuditAction.Restore,
            clientId: clientId,
            resourceType: nameof(StoredFile),
            resourceId: file.Id,
            metadata: new { file.OriginalName, file.FolderId });

        await context.SaveChangesAsync(cancellationToken);
    }
}
