namespace FileStore.Domain.Enums;

public enum EmailStatus
{
    Pending = 1,
    Sent = 2,

    /// <summary>Se agotaron los reintentos. No se vuelve a intentar solo.</summary>
    Failed = 3
}
