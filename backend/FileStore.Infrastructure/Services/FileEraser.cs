using FileStore.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FileStore.Infrastructure.Services;

public class FileEraser(
    IApplicationDbContext context,
    IStorageService storage,
    ILogger<FileEraser> logger)
    : IFileEraser
{
    public async Task<long> EraseAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await context.Files
            .FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);

        if (file is null)
        {
            return 0;
        }

        var versions = await context.FileVersions
            .Where(v => v.FileId == fileId)
            .ToListAsync(cancellationToken);

        var paths = versions.Select(v => v.StoragePath).ToList();
        var freedBytes = versions.Sum(v => v.SizeBytes);
        var clientId = file.ClientId;

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);

        // Files apunta a FileVersions por CurrentVersionId con NoAction, asi que
        // hay que soltar ese puntero ANTES de borrar las versiones o la clave
        // foranea bloquea el borrado.
        file.CurrentVersionId = null;
        await context.SaveChangesAsync(cancellationToken);

        context.FileVersions.RemoveRange(versions);
        context.Files.Remove(file);
        await context.SaveChangesAsync(cancellationToken);

        // La cuota se libera aca porque los binarios ya no son alcanzables.
        await context.Clients
            .Where(c => c.Id == clientId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.UsedBytes, c => c.UsedBytes - freedBytes),
                cancellationToken);

        await context.CommitTransactionAsync(cancellationToken);

        // Los binarios se borran DESPUES de confirmar la transaccion. Al reves,
        // si la base fallara quedarian filas apuntando a archivos inexistentes,
        // que es peor que un binario huerfano: el cliente veria el archivo en el
        // listado y la descarga fallaria.
        foreach (var path in paths)
        {
            try
            {
                await storage.DeleteAsync(path, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "No se pudo borrar el binario {StoragePath} del archivo {FileId}. " +
                    "Queda huerfano en disco.",
                    path,
                    fileId);
            }
        }

        return freedBytes;
    }
}
