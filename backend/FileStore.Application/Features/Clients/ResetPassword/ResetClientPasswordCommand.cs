using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Clients.ResetPassword;

public record ResetClientPasswordCommand(Guid Id) : IRequest<string>;

public class ResetClientPasswordCommandHandler(
    IApplicationDbContext context,
    IPasswordHasher passwordHasher,
    IPasswordGenerator passwordGenerator,
    IAuditLogger auditLogger)
    : IRequestHandler<ResetClientPasswordCommand, string>
{
    public async Task<string> Handle(
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

        await context.SaveChangesAsync(cancellationToken);

        return password;
    }
}
