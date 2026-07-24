using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Trash;

public record HardDeleteCommand(Guid Id) : IRequest;

public class HardDeleteCommandHandler(
    IApplicationDbContext context,
    IFileEraser eraser,
    ICurrentUser currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<HardDeleteCommand>
{
    public async Task Handle(HardDeleteCommand request, CancellationToken cancellationToken)
    {
        var clientId = currentUser.ClientId ?? throw new InvalidCredentialsException();

        // Solo se puede eliminar definitivamente lo que ya esta en la papelera:
        // evita que un error de un solo clic destruya un archivo en uso.
        var file = await context.Files
            .Where(f => f.Id == request.Id && f.ClientId == clientId && f.IsDeleted)
            .Select(f => new { f.Id, f.OriginalName })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(StoredFile), request.Id);

        // La auditoria se escribe ANTES de borrar: despues, el archivo ya no
        // existe y no habria de que dejar constancia.
        auditLogger.Record(
            AuditAction.HardDelete,
            clientId: clientId,
            resourceType: nameof(StoredFile),
            resourceId: file.Id,
            metadata: new { file.OriginalName });

        await context.SaveChangesAsync(cancellationToken);

        await eraser.EraseAsync(file.Id, cancellationToken);
    }
}
