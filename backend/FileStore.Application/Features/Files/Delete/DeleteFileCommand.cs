using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Files.Delete;

public record DeleteFileCommand(Guid Id) : IRequest;

public class DeleteFileCommandHandler(
    IApplicationDbContext context,
    ICurrentUser currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<DeleteFileCommand>
{
    public async Task Handle(DeleteFileCommand request, CancellationToken cancellationToken)
    {
        var clientId = currentUser.ClientId ?? throw new InvalidCredentialsException();

        var file = await context.Files
            .FirstOrDefaultAsync(
                f => f.Id == request.Id && f.ClientId == clientId && !f.IsDeleted,
                cancellationToken)
            ?? throw new NotFoundException(nameof(StoredFile), request.Id);

        // Borrado suave: el archivo va a la papelera y SIGUE OCUPANDO CUOTA
        // (decision 1 de la seccion 15). Que el borrado no libere espacio es
        // deliberado: obliga a vaciar la papelera o esperar la purga, y evita
        // que se acumulen datos invisibles.
        file.IsDeleted = true;
        file.DeletedAt = DateTime.UtcNow;
        file.UpdatedAt = DateTime.UtcNow;

        auditLogger.Record(
            AuditAction.Delete,
            clientId: clientId,
            resourceType: nameof(StoredFile),
            resourceId: file.Id,
            metadata: new { file.OriginalName, file.SizeBytes });

        await context.SaveChangesAsync(cancellationToken);
    }
}
