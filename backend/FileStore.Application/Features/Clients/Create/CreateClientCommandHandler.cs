using FileStore.Application.Abstractions;
using FileStore.Application.Common.Emails;
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
    IAuditLogger auditLogger,
    IEmailQueue emailQueue,
    IAppUrlProvider urls)
    : IRequestHandler<CreateClientCommand, ClientDto>
{
    public async Task<ClientDto> Handle(
        CreateClientCommand request,
        CancellationToken cancellationToken)
    {
        // Se comprueba ANTES de tocar nada: si el correo no puede salir, la
        // contraseña generada no llegaria a ningun lado y la cuenta naceria
        // inaccesible. Mejor no crearla.
        if (!emailQueue.IsDeliveryConfigured)
        {
            throw new EmailNotConfiguredException("dar de alta un cliente");
        }

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

        // Se encola, no se envia: la fila del correo se confirma en el mismo
        // SaveChanges que el cliente. Si el alta fallara aqui, no se manda la
        // contraseña de una cuenta que nunca existio.
        emailQueue.Enqueue(
            EmailTemplates.Welcome(client.Email, client.Name, password, urls.PanelUrl),
            client.Id);

        await context.SaveChangesAsync(cancellationToken);

        // La contraseña no vuelve en la respuesta: solo la recibe el cliente por
        // correo. Devolverla obligaba al super-admin a hacersela llegar por su
        // cuenta, y ese canal (chat, etc.) es el eslabon mas debil de todo esto.
        return ClientDto.From(client);
    }
}
