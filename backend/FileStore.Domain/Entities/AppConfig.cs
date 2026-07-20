namespace FileStore.Domain.Entities;

/// <summary>
/// Configuracion global clave/valor. La PK es la clave: no hay Id sustituto
/// porque la clave ya identifica la fila de forma natural y estable.
/// </summary>
public class AppConfig
{
    public string Key { get; set; } = null!;
    public string Value { get; set; } = null!;
    public DateTime UpdatedAt { get; set; }
}
