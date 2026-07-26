using FileStore.Application.Abstractions;
using FileStore.Application.Common.Emails;
using FileStore.Application.Common.Exceptions;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Clients.ResetPassword;

/// <summary>
/// No devuelve nada: la contraseña nueva viaja por correo al cliente, no por la
/// respuesta HTTP al super-admin.
/// </summary>
public record ResetClientPasswordCommand(Guid Id) : IRequest;

public class ResetClientPasswordCommandHandler(
    IApplicationDbContext context,
    IPasswordHasher passwordHasher,
    IPasswordGenerator passwordGenerator,
    IAuditLogger auditLogger,
    IEmailQueue emailQueue,
    IAppUrlProvider urls)
    : IRequestHandler<ResetClientPasswordCommand>
{
    public async Task Handle(
        ResetClientPasswordCommand request,
        CancellationToken cancellationToken)
    {
        var client = await context.Clients
            .FirstOrDefaultAsync(c => c.Id == request.Id && !c.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Client), request.Id);

        var password = passwordGenerator.Generate();

        client.PasswordHash = passwordHasher.Hash(password);
        client.UpdatedAt = DateTime.UtcNow;

        // Un reseteo suele responder a una credencial comprometida, asi que se
        // cierran todas las sesiones: dejarlas vivas haria inutil el cambio.
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
            AuditAction.ChangePassword,
            clientId: client.Id,
            resourceType: nameof(Client),
            resourceId: client.Id,
            metadata: new { Reset = true, RevokedSessions = sessions.Count });

        emailQueue.Enqueue(
            EmailTemplates.PasswordReset(client.Email, client.Name, password, urls.PanelUrl),
            client.Id);

        await context.SaveChangesAsync(cancellationToken);
    }
}
