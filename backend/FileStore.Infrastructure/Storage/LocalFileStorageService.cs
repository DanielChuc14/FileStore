using FileStore.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace FileStore.Infrastructure.Storage;

public class StorageSettings
{
    public const string SectionName = "Storage";

    /// <summary>Raiz del almacenamiento fisico. Configurable por entorno.</summary>
    public string BasePath { get; set; } = null!;
}

/// <summary>
/// Almacenamiento en el disco local. Estructura:
///   {basePath}/{clientId}/{yyyy}/{MM}/{fileVersionId}.bin
///
/// El nombre original NUNCA forma parte de la ruta: evita colisiones, caracteres
/// invalidos del sistema de archivos y, sobre todo, path traversal. Fragmentar
/// por año y mes evita directorios con millones de entradas.
/// </summary>
public class LocalFileStorageService : IStorageService
{
    private readonly string _basePath;

    public LocalFileStorageService(IOptions<StorageSettings> options)
    {
        _basePath = Path.GetFullPath(options.Value.BasePath);
        Directory.CreateDirectory(_basePath);
    }

    public async Task<string> SaveAsync(
        Guid clientId,
        Guid fileVersionId,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var relativePath = Path.Combine(
            clientId.ToString(),
            now.ToString("yyyy"),
            now.ToString("MM"),
            $"{fileVersionId}.bin");

        var fullPath = ResolveFullPath(relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        // FileMode.CreateNew y no Create: si la ruta ya existiera seria un Guid
        // repetido, y sobrescribir en silencio destruiria datos de otra version.
        await using var target = new FileStream(
            fullPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        await content.CopyToAsync(target, cancellationToken);

        // Se normaliza a barras para que la ruta guardada en la base no dependa
        // del sistema operativo donde se subio.
        return relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }

    public Task<Stream> OpenReadAsync(
        string storagePath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveFullPath(storagePath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Stored file is missing.", storagePath);
        }

        Stream stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveFullPath(storagePath);

        // Delete no falla si el archivo no existe, que es justo lo que queremos:
        // la purga y la limpieza de huerfanos tienen que poder reintentarse.
        File.Delete(fullPath);

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(File.Exists(ResolveFullPath(storagePath)));

    /// <summary>
    /// Ultima linea de defensa contra path traversal: aunque la ruta se arme
    /// siempre a partir de Guid, se verifica que el resultado caiga dentro de
    /// basePath. Si alguna vez entra un valor manipulado desde la base, no sale
    /// del directorio de almacenamiento.
    /// </summary>
    private string ResolveFullPath(string relativePath)
    {
        var combined = Path.GetFullPath(Path.Combine(_basePath, relativePath));

        if (!combined.StartsWith(_basePath, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                $"Resolved path escapes the storage root: '{relativePath}'.");
        }

        return combined;
    }
}
