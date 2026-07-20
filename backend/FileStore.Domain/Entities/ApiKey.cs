namespace FileStore.Domain.Entities;

public class ApiKey
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }

    /// <summary>Nombre libre para identificarla en el panel: prod, staging, mobile-app.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Hash del valor completo. El secreto en claro nunca se persiste.</summary>
    public string KeyHash { get; set; } = null!;

    /// <summary>Parte visible de la key (ej: fs_live_ab12). Permite identificarla sin exponer el secreto.</summary>
    public string Prefix { get; set; } = null!;

    /// <summary>Override opcional del default global. Null = usa el valor de AppConfig.</summary>
    public int? RateLimitPerMinute { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime? LastUsedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public Client Client { get; set; } = null!;
}
