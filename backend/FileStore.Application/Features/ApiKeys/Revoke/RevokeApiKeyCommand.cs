using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.ApiKeys.Revoke;

public record RevokeApiKeyCommand(Guid Id) : IRequest;

public class RevokeApiKeyCommandHandler(
    IApplicationDbContext context,
    ICurrentUser currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<RevokeApiKeyCommand>
{
    public async Task Handle(RevokeApiKeyCommand request, CancellationToken cancellationToken)
    {
        var clientId = currentUser.ClientId ?? throw new InvalidCredentialsException();

        var apiKey = await context.ApiKeys
            .FirstOrDefaultAsync(k => k.Id == request.Id && k.ClientId == clientId, cancellationToken)
            ?? throw new NotFoundException(nameof(ApiKey), request.Id);

        if (apiKey.RevokedAt is not null)
        {
            // Idempotente: revocar dos veces no es un error.
            return;
        }

        apiKey.IsActive = false;
        apiKey.RevokedAt = DateTime.UtcNow;

        auditLogger.Record(
            AuditAction.RevokeApiKey,
            clientId: clientId,
            resourceType: nameof(ApiKey),
            resourceId: apiKey.Id,
            metadata: new { apiKey.Name, apiKey.Prefix });

        await context.SaveChangesAsync(cancellationToken);
    }
}
