using FileStore.Application.Abstractions;
using FileStore.Application.Common.Exceptions;
using FileStore.Application.Features.Clients.Common;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FileStore.Application.Features.Clients.Update;

/// <summary>
/// Semantica PATCH: null significa "no cambiar", no "poner en null". Para
/// limpiar un override (volver al default global) hay que usar el flag
/// correspondiente, porque null solo no permite distinguir ambos casos.
/// </summary>
public record UpdateClientCommand(
    Guid Id,
    string? Name,
    long? QuotaBytes,
    bool? IsActive,
    int? TrashRetentionDays,
    long? MaxFileSizeBytes,
    bool ClearTrashRetentionOverride = false,
    bool ClearMaxFileSizeOverride = false) : IRequest<ClientDto>;

public class UpdateClientCommandValidator : AbstractValidator<UpdateClientCommand>
{
    public UpdateClientCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200).When(c => c.Name is not null);
        RuleFor(c => c.QuotaBytes).GreaterThan(0).When(c => c.QuotaBytes.HasValue);
        RuleFor(c => c.TrashRetentionDays).InclusiveBetween(1, 365).When(c => c.TrashRetentionDays.HasValue);
        RuleFor(c => c.MaxFileSizeBytes).GreaterThan(0).When(c => c.MaxFileSizeBytes.HasValue);
    }
}

public class UpdateClientCommandHandler(
    IApplicationDbContext context,
    IAuditLogger auditLogger)
    : IRequestHandler<UpdateClientCommand, ClientDto>
{
    public async Task<ClientDto> Handle(
        UpdateClientCommand request,
        CancellationToken cancellationToken)
    {
        var client = await context.Clients
            .FirstOrDefaultAsync(c => c.Id == request.Id && !c.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Client), request.Id);

        var changes = new Dictionary<string, object?>();

        if (request.Name is not null && request.Name != client.Name)
        {
            changes[nameof(client.Name)] = request.Name;
            client.Name = request.Name.Trim();
        }

        if (request.QuotaBytes.HasValue && request.QuotaBytes != client.QuotaBytes)
        {
            // No se valida contra UsedBytes: bajar la cuota por debajo de lo ya
            // consumido es legitimo (impide subir mas, sin borrar nada).
            changes[nameof(client.QuotaBytes)] = request.QuotaBytes;
            client.QuotaBytes = request.QuotaBytes.Value;
        }

        if (request.IsActive.HasValue && request.IsActive != client.IsActive)
        {
            changes[nameof(client.IsActive)] = request.IsActive;
            client.IsActive = request.IsActive.Value;
        }

        if (request.ClearTrashRetentionOverride)
        {
            changes[nameof(client.TrashRetentionDays)] = null;
            client.TrashRetentionDays = null;
        }
        else if (request.TrashRetentionDays.HasValue)
        {
            changes[nameof(client.TrashRetentionDays)] = request.TrashRetentionDays;
            client.TrashRetentionDays = request.TrashRetentionDays;
        }

        if (request.ClearMaxFileSizeOverride)
        {
            changes[nameof(client.MaxFileSizeBytes)] = null;
            client.MaxFileSizeBytes = null;
        }
        else if (request.MaxFileSizeBytes.HasValue)
        {
            changes[nameof(client.MaxFileSizeBytes)] = request.MaxFileSizeBytes;
            client.MaxFileSizeBytes = request.MaxFileSizeBytes;
        }

        if (changes.Count == 0)
        {
            return ClientDto.From(client);
        }

        client.UpdatedAt = DateTime.UtcNow;

        // Bloquear y desbloquear se auditan como acciones propias: son las que
        // mas interesa poder rastrear despues.
        var action = changes.ContainsKey(nameof(client.IsActive))
            ? AuditAction.BlockClient
            : AuditAction.UpdateClient;

        auditLogger.Record(
            action,
            clientId: client.Id,
            resourceType: nameof(Client),
            resourceId: client.Id,
            metadata: changes);

        await context.SaveChangesAsync(cancellationToken);

        return ClientDto.From(client);
    }
}
