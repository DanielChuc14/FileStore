using FileStore.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace FileStore.UnitTests.Infrastructure;

public class LocalFileStorageServiceTests : IDisposable
{
    private readonly string _root;
    private readonly LocalFileStorageService _storage;

    public LocalFileStorageServiceTests()
    {
        // Directorio temporal aislado por ejecucion.
        _root = Path.Combine(Path.GetTempPath(), $"filestore-tests-{Guid.NewGuid():N}");
        _storage = new LocalFileStorageService(
            Options.Create(new StorageSettings { BasePath = _root }));
    }

    [Fact]
    public async Task OpenReadAsync_RutaConTraversal_NoEscapaDeLaRaiz()
    {
        // Ruta manipulada que intenta salir de la raiz de almacenamiento.
        var escaping = Path.Combine("..", "..", "etc", "passwd");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _storage.OpenReadAsync(escaping));
    }

    [Fact]
    public async Task OpenReadAsync_DirectorioHermanoConPrefijoComun_NoEscapa()
    {
        // El caso que un StartsWith sin separador dejaria pasar: un hermano cuyo
        // nombre empieza igual que la raiz (".../<root>" vs ".../<root>-evil").
        var sibling = Path.Combine("..", $"{Path.GetFileName(_root)}-evil", "secreto.bin");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _storage.OpenReadAsync(sibling));
    }

    [Fact]
    public async Task SaveYOpenRead_RutaValida_DevuelveElContenido()
    {
        var clientId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var original = new byte[] { 1, 2, 3, 4, 5 };

        using var input = new MemoryStream(original);
        var storagePath = await _storage.SaveAsync(clientId, versionId, input);

        await using var output = await _storage.OpenReadAsync(storagePath);
        using var buffer = new MemoryStream();
        await output.CopyToAsync(buffer);

        Assert.Equal(original, buffer.ToArray());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
