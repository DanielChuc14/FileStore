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
            ActorType = currentUser.UserType switch
            {
                UserType.SuperAdmin => ActorType.SuperAdmin,
                UserType.Client => ActorType.Client,
                _ => ActorType.Client
            },
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
