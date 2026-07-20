using FileStore.Domain.Enums;

namespace FileStore.Domain.Entities;

public class AuditLog
{
    public Guid Id { get; set; }

    /// <summary>Null en acciones globales del super-admin que no afectan a un cliente concreto.</summary>
    public Guid? ClientId { get; set; }

    public ActorType ActorType { get; set; }

    /// <summary>Id del Client, ApiKey o SuperAdmin que ejecuto la accion.</summary>
    public Guid ActorId { get; set; }

    public AuditAction Action { get; set; }

    public string? ResourceType { get; set; }
    public Guid? ResourceId { get; set; }

    /// <summary>Contexto adicional en JSON (nombres, rutas, tamaños). Columna jsonb.</summary>
    public string? MetadataJson { get; set; }

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; }

    public Client? Client { get; set; }
}
