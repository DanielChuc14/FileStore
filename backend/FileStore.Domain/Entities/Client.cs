namespace FileStore.Domain.Entities;

public class Client
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string Name { get; set; } = null!;

    public long QuotaBytes { get; set; }
    public long UsedBytes { get; set; }

    /// <summary>Bloqueo reversible: el cliente no puede autenticarse pero conserva todo.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Baja de la cuenta. Distinto de IsActive: bloquear es temporal y
    /// reversible, dar de baja saca al cliente del listado. Los datos siguen en
    /// la base porque el audit log los referencia.
    /// </summary>
    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    /// <summary>Override opcional del default global. Null = usa el valor de AppConfig.</summary>
    public int? TrashRetentionDays { get; set; }

    /// <summary>Override opcional del default global. Null = usa el valor de AppConfig.</summary>
    public long? MaxFileSizeBytes { get; set; }

    /// <summary>
    /// Ultimo umbral de cuota por el que ya se aviso (80 o 95), o null si no se
    /// aviso nada. Evita repetir el correo en cada ciclo del job: sin esta marca
    /// un cliente al 96%% recibiria un aviso cada hora. Vuelve a null cuando el
    /// consumo baja del 80%%, para que un aviso futuro si se envie.
    /// </summary>
    public int? QuotaAlertPercent { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ApiKey> ApiKeys { get; set; } = [];
    public ICollection<Folder> Folders { get; set; } = [];
    public ICollection<StoredFile> Files { get; set; } = [];
}
