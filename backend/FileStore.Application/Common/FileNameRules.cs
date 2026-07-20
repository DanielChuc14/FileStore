namespace FileStore.Application.Common;

/// <summary>
/// Reglas para nombres de archivo y carpeta que vienen del usuario.
///
/// El nombre original nunca toca el disco (la ruta fisica se arma con Guid),
/// asi que un traversal no podria escapar del almacenamiento. Aun asi se valida:
/// el nombre se devuelve en Content-Disposition al descargar, se muestra en el
/// panel y podria terminar en el sistema de archivos de quien integra la API.
/// </summary>
public static class FileNameRules
{
    public const int MaxLength = 255;

    private static readonly char[] Forbidden =
        ['/', '\\', ':', '*', '?', '"', '<', '>', '|', '\0'];

    /// <summary>Nombres reservados en Windows. Rompen a quien descargue en ese sistema.</summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static bool IsValid(string? name) => Validate(name) is null;

    /// <summary>Devuelve el motivo del rechazo, o null si el nombre es valido.</summary>
    public static string? Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "El nombre no puede estar vacio.";
        }

        var trimmed = name.Trim();

        if (trimmed.Length > MaxLength)
        {
            return $"El nombre no puede superar los {MaxLength} caracteres.";
        }

        // "." y ".." son entradas de directorio, no nombres validos.
        if (trimmed is "." or "..")
        {
            return "El nombre no es valido.";
        }

        if (trimmed.IndexOfAny(Forbidden) >= 0)
        {
            return "El nombre contiene caracteres no permitidos.";
        }

        // Caracteres de control: invisibles, y se prestan a inyeccion en headers.
        if (trimmed.Any(char.IsControl))
        {
            return "El nombre contiene caracteres de control.";
        }

        var withoutExtension = Path.GetFileNameWithoutExtension(trimmed);
        if (ReservedNames.Contains(withoutExtension))
        {
            return "El nombre esta reservado por el sistema operativo.";
        }

        // Windows no permite terminar en punto ni espacio, y los recorta en
        // silencio: dos archivos distintos colisionarian al descargarlos.
        if (trimmed.EndsWith('.') || name.EndsWith(' '))
        {
            return "El nombre no puede terminar en punto ni en espacio.";
        }

        return null;
    }

    /// <summary>Extension en minusculas y sin punto. Cadena vacia si no tiene.</summary>
    public static string GetExtension(string fileName) =>
        Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
}
