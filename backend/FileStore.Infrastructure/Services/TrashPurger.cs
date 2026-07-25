using FileStore.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FileStore.Infrastructure.Services;

/// <summary>
/// Logica de barrido de la papelera, extraida del BackgroundService que la agenda.
/// Aqui vive la regla de "que archivo ya vencio" y el borrado; el temporizador es
/// otra responsabilidad.
/// </summary>
public class TrashPurger(
    IApplicationDbContext context,
    IAppConfigReader appConfig,
    IFileEraser eraser,
    ILogger<TrashPurger> logger)
    : ITrashPurger
{
    public async Task<TrashPurgeResult> PurgeExpiredAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var globalRetention = await appConfig.GetTrashRetentionDaysAsync(cancellationToken);
        var now = DateTime.UtcNow;

        // Se traen los candidatos con la retencion de su cliente y el corte se
        // evalua en memoria: la retencion efectiva depende de un override
        // nullable por cliente, que no se traduce a SQL de forma confiable.
        var candidates = await context.Files
            .AsNoTracking()
            .Where(f => f.IsDeleted && f.DeletedAt != null)
            .Select(f => new
            {
                f.Id,
                f.ClientId,
                f.OriginalName,
                DeletedAt = f.DeletedAt!.Value,
                Retention = f.Client.TrashRetentionDays
            })
            .Take(batchSize * 5)
            .ToListAsync(cancellationToken);

        var expired = candidates
            .Where(f => f.DeletedAt.AddDays(f.Retention ?? globalRetention) <= now)
            .Take(batchSize)
            .ToList();

        if (expired.Count == 0)
        {
            return new TrashPurgeResult(0, 0);
        }

        logger.LogInformation("Purgando {Count} archivos vencidos.", expired.Count);

        long totalFreed = 0;

        foreach (var file in expired)
        {
            try
            {
                // Cada archivo se borra por separado: si uno falla, los demas
                // igual se purgan.
                totalFreed += await eraser.EraseAsync(file.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "No se pudo purgar el archivo {FileId} ({Name}) del cliente {ClientId}.",
                    file.Id,
                    file.OriginalName,
                    file.ClientId);
            }
        }

        logger.LogInformation(
            "Purga completada: {Count} archivos, {Bytes} bytes liberados.",
            expired.Count,
            totalFreed);

        return new TrashPurgeResult(expired.Count, totalFreed);
    }
}
