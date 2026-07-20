using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Application.Features.ApiKeys.Common;
using FileStore.Application.Features.ApiKeys.Create;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.ApiKeys.Rotate;

public record RotateApiKeyCommand(Guid Id) : IRequest<CreateApiKeyResult>;

/// <summary>
/// Rotar es revocar la key vieja y emitir una nueva con el mismo nombre y rate
/// limit. Se hace en una sola operacion para que el cliente pueda desplegar la
/// nueva sin quedarse sin acceso en el medio.
/// </summary>
public class RotateApiKeyCommandHandler(
    IApplicationDbContext context,
    IApiKeyGenerator generator,
    ICurrentUser currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<RotateApiKeyCommand, CreateApiKeyResult>
{
    public async Task<CreateApiKeyResult> Handle(
        RotateApiKeyCommand request,
        CancellationToken cancellationToken)
    {
        var clientId = currentUser.UserId ?? throw new InvalidCredentialsException();

        var existing = await context.ApiKeys
            .FirstOrDefaultAsync(k => k.Id == request.Id && k.ClientId == clientId, cancellationToken)
            ?? throw new NotFoundException(nameof(ApiKey), request.Id);

        var generated = generator.Generate();

        var replacement = new ApiKey
        {
            Id = Guid.CreateVersion7(),
            ClientId = clientId,
            Name = existing.Name,
            KeyHash = generated.Hash,
            Prefix = generated.Prefix,
            RateLimitPerMinute = existing.RateLimitPerMinute,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        existing.IsActive = false;
        existing.RevokedAt = DateTime.UtcNow;

        context.ApiKeys.Add(replacement);

        auditLogger.Record(
            AuditAction.RotateApiKey,
            clientId: clientId,
            resourceType: nameof(ApiKey),
            resourceId: replacement.Id,
            metadata: new
            {
                RevokedPrefix = existing.Prefix,
                NewPrefix = replacement.Prefix,
                replacement.Name
            });

        await context.SaveChangesAsync(cancellationToken);

        return new CreateApiKeyResult(
            CreateApiKeyCommandHandler.ToDto(replacement),
            generated.Value);
    }
}
