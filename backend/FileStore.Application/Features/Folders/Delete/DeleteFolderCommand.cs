using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Folders.Delete;

public record DeleteFolderCommand(Guid Id, bool Recursive = false) : IRequest;

public class DeleteFolderCommandHandler(
    IApplicationDbContext context,
    ICurrentUser currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<DeleteFolderCommand>
{
    public async Task Handle(DeleteFolderCommand request, CancellationToken cancellationToken)
    {
        var clientId = currentUser.ClientId ?? throw new InvalidCredentialsException();

        var folder = await context.Folders
            .FirstOrDefaultAsync(f => f.Id == request.Id && f.ClientId == clientId, cancellationToken)
            ?? throw new NotFoundException(nameof(Folder), request.Id);

        var descendants = await context.Folders
            .Where(f => f.ClientId == clientId && f.Path.StartsWith(folder.Path + "/"))
            .ToListAsync(cancellationToken);

        var folderIds = descendants.Select(f => f.Id).Append(folder.Id).ToList();

        // Se traen TODOS los archivos de esas carpetas, incluidos los que ya
        // estan en la papelera: siguen apuntando a la carpeta por FK y, si no se
        // desvinculan, el DELETE falla por violacion de clave foranea.
        var files = await context.Files
            .Where(f => f.ClientId == clientId
                && f.FolderId != null
                && folderIds.Contains(f.FolderId!.Value))
            .ToListAsync(cancellationToken);

        var activeFiles = files.Where(f => !f.IsDeleted).ToList();

        if (!request.Recursive && (descendants.Count > 0 || activeFiles.Count > 0))
        {
            throw new ConflictException(
                "La carpeta no esta vacia. Usa recursive=true para borrarla con su contenido.");
        }

        // Los archivos van a la papelera, no se borran del disco: siguen ocupando
        // cuota hasta que se purguen o el cliente los elimine definitivamente.
        // Borrar la carpeta no debe destruir contenido de forma irreversible.
        var now = DateTime.UtcNow;

        foreach (var file in files)
        {
            // Los que ya estaban en la papelera conservan su fecha de borrado
            // original: de lo contrario se les reiniciaria el plazo de purga.
            if (!file.IsDeleted)
            {
                file.IsDeleted = true;
                file.DeletedAt = now;
                file.UpdatedAt = now;
            }

            file.FolderId = null;
        }

        context.Folders.RemoveRange(descendants);
        context.Folders.Remove(folder);

        auditLogger.Record(
            AuditAction.DeleteFolder,
            clientId: clientId,
            resourceType: nameof(Folder),
            resourceId: folder.Id,
            metadata: new
            {
                folder.Path,
                DeletedSubfolders = descendants.Count,
                TrashedFiles = activeFiles.Count,
                DetachedFiles = files.Count
            });

        await context.SaveChangesAsync(cancellationToken);
    }
}
