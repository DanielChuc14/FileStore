namespace FileStore.Application.Abstractions;

/// <summary>
/// Almacenamiento binario. Opera sobre Stream y no sobre byte[] ni rutas del
/// sistema: asi un archivo grande nunca se carga entero en memoria, y una
/// implementacion con cifrado puede envolver a esta como decorador sin que
/// ningun handler cambie.
/// </summary>
public interface IStorageService
{
    /// <summary>
    /// Persiste el contenido y devuelve la ruta relativa donde quedo.
    /// La ruta la decide el almacenamiento, no quien lo llama.
    /// </summary>
    Task<string> SaveAsync(
        Guid clientId,
        Guid fileVersionId,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default);

    /// <summary>Borra el binario. No falla si ya no existe: la purga debe ser idempotente.</summary>
    Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken = default);
}
