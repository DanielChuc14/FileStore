using System.Text.Json;
using FileStore.Application.Abstractions;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;

namespace FileStore.Infrastructure.Services;

public class AuditLogger(IApplicationDbContext context, ICurrentUser currentUser) : IAuditLogger
{
    public void Record(
        AuditAction action,
        Guid? clientId = null,
        string? resourceType = null,
        Guid? resourceId = null,
        object? metadata = null)
    {
        context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.CreateVersion7(),
            ClientId = clientId,
            // El canal se decide por la API Key y no por UserType: el esquema de
            // API Key no emite claim de tipo de usuario, asi que mirando solo
            // UserType toda peticion de una integracion caia en el default y se
            // registraba como Client. ActorType.ApiKey no lo escribia nadie.
            ActorType = currentUser.ApiKeyId is not null
                ? ActorType.ApiKey
                : currentUser.UserType switch
                {
                    UserType.SuperAdmin => ActorType.SuperAdmin,
                    UserType.Client => ActorType.Client,
                    _ => ActorType.Client
                },

            // ActorId sigue siendo el cliente, tambien con API Key: es quien
            // responde por la accion. Que key concreta se uso se reconstruye por
            // otro lado (FileVersion.UploadedByApiKeyId y el metadata de las
            // operaciones sobre keys).
            ActorId = currentUser.UserId ?? Guid.Empty,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata),
            IpAddress = currentUser.IpAddress,
            UserAgent = currentUser.UserAgent,
            CreatedAt = DateTime.UtcNow
        });
    }
}
