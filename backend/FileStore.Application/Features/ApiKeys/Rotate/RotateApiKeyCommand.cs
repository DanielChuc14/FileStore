using FileStore.Application.Abstractions;
using FileStore.Application.Common.Emails;
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
    IAuditLogger auditLogger,
    IEmailQueue emailQueue,
    IAppUrlProvider urls)
    : IRequestHandler<RotateApiKeyCommand, CreateApiKeyResult>
{
    public async Task<CreateApiKeyResult> Handle(
        RotateApiKeyCommand request,
        CancellationToken cancellationToken)
    {
        var clientId = currentUser.ClientId ?? throw new InvalidCredentialsException();

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

        await NotifyAsync(clientId, replacement.Name, replacement.Prefix, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        return new CreateApiKeyResult(
            CreateApiKeyCommandHandler.ToDto(replacement),
            generated.Value);
    }

    /// <summary>
    /// Avisa al cliente de que se emitio una credencial nueva. Si la rotacion no
    /// la pidio el, es la señal de que alguien tiene acceso a su panel.
    /// </summary>
    private async Task NotifyAsync(
        Guid clientId,
        string keyName,
        string prefix,
        CancellationToken cancellationToken)
    {
        var client = await context.Clients
            .Where(c => c.Id == clientId)
            .Select(c => new { c.Email, c.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (client is null)
        {
            return;
        }

        emailQueue.Enqueue(
            EmailTemplates.ApiKeyActivity(
                client.Email, client.Name, keyName, prefix, rotated: true, urls.PanelUrl),
            clientId);
    }
}
