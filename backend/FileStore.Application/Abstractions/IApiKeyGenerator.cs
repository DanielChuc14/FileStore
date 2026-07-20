namespace FileStore.Application.Abstractions;

public record GeneratedApiKey(string Value, string Prefix, string Hash);

public interface IApiKeyGenerator
{
    /// <summary>
    /// Genera una API Key nueva. El Value completo se devuelve una unica vez al
    /// crearla; en la base solo quedan Prefix (visible) y Hash.
    /// </summary>
    GeneratedApiKey Generate();

    /// <summary>
    /// Extrae el prefijo de una key presentada en un request, para poder
    /// buscarla por indice sin recorrer toda la tabla hasheando.
    /// Devuelve null si la key no tiene el formato esperado.
    /// </summary>
    string? ExtractPrefix(string presentedKey);

    string Hash(string presentedKey);
}
