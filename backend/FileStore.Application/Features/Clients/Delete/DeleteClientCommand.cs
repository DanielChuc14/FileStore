using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Clients.Delete;

public record DeleteClientCommand(Guid Id) : IRequest;

public class DeleteClientCommandHandler(
    IApplicationDbContext context,
    IAuditLogger auditLogger)
    : IRequestHandler<DeleteClientCommand>
{
    public async Task Handle(DeleteClientCommand request, CancellationToken cancellationToken)
    {
        var client = await context.Clients
            .FirstOrDefaultAsync(c => c.Id == request.Id && !c.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Client), request.Id);

        client.IsDeleted = true;
        client.DeletedAt = DateTime.UtcNow;
        client.UpdatedAt = DateTime.UtcNow;

        // Tambien se desactiva: de lo contrario la cuenta seguiria pudiendo
        // autenticarse pese a estar dada de baja.
        client.IsActive = false;

        // Se revocan las sesiones abiertas. Sin esto, un access token vigente
        // seguiria funcionando hasta 15 minutos despues de la baja.
        var sessions = await context.RefreshTokens
            .Where(t => t.UserId == client.Id
                && t.UserType == UserType.Client
                && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.RevokedAt = DateTime.UtcNow;
        }

        auditLogger.Record(
            AuditAction.DeleteClient,
            clientId: client.Id,
            resourceType: nameof(Client),
            resourceId: client.Id,
            metadata: new { client.Email, RevokedSessions = sessions.Count });

        await context.SaveChangesAsync(cancellationToken);
    }
}
