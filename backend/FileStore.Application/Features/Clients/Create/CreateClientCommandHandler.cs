using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Application.Features.Clients.Common;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Clients.Create;

public class CreateClientCommandHandler(
    IApplicationDbContext context,
    IPasswordHasher passwordHasher,
    IPasswordGenerator passwordGenerator,
    IAuditLogger auditLogger)
    : IRequestHandler<CreateClientCommand, CreateClientResult>
{
    public async Task<CreateClientResult> Handle(
        CreateClientCommand request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        // El email tambien identifica super-admins: si se repitiera, el login no
        // sabria contra que tabla resolver.
        var emailTaken =
            await context.Clients.AnyAsync(c => c.Email == email, cancellationToken) ||
            await context.SuperAdmins.AnyAsync(a => a.Email == email, cancellationToken);

        if (emailTaken)
        {
            throw new ConflictException($"El email '{email}' ya esta registrado.");
        }

        var password = passwordGenerator.Generate();
        var now = DateTime.UtcNow;

        var client = new Client
        {
            Id = Guid.CreateVersion7(),
            Email = email,
            Name = request.Name.Trim(),
            PasswordHash = passwordHasher.Hash(password),
            QuotaBytes = request.QuotaBytes,
            UsedBytes = 0,
            IsActive = true,
            IsDeleted = false,
            TrashRetentionDays = request.TrashRetentionDays,
            MaxFileSizeBytes = request.MaxFileSizeBytes,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.Clients.Add(client);

        auditLogger.Record(
            AuditAction.CreateClient,
            clientId: client.Id,
            resourceType: nameof(Client),
            resourceId: client.Id,
            metadata: new { client.Email, client.Name, client.QuotaBytes });

        await context.SaveChangesAsync(cancellationToken);

        return new CreateClientResult(ClientDto.From(client), password);
    }
}
