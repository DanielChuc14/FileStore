using FileStore.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FileStore.Infrastructure.BackgroundJobs;

public class TrashPurgeSettings
{
    public const string SectionName = "TrashPurge";

    public bool Enabled { get; set; } = true;

    /// <summary>Cada cuanto se ejecuta el barrido.</summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>Tope de archivos por corrida, para que un barrido no monopolice la base.</summary>
    public int BatchSize { get; set; } = 100;
}

/// <summary>
/// Purga los archivos cuya permanencia en la papelera vencio: borra sus binarios
/// y libera la cuota que seguian ocupando.
/// </summary>
public class TrashPurgeService(
    IServiceScopeFactory scopeFactory,
    IOptions<TrashPurgeSettings> options,
    ILogger<TrashPurgeService> logger)
    : BackgroundService
{
    private readonly TrashPurgeSettings _settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            logger.LogInformation("La purga de papelera esta deshabilitada.");
            return;
        }

        var interval = TimeSpan.FromMinutes(_settings.IntervalMinutes);
        logger.LogInformation("Purga de papelera activa, cada {Interval}.", interval);

        using var timer = new PeriodicTimer(interval);

        // Se corre una vez al arrancar y despues por intervalo: si el proceso
        // estuvo caido, no hay que esperar un ciclo completo para ponerse al dia.
        do
        {
            try
            {
                await PurgeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Un fallo no puede matar el servicio: se registra y se reintenta
                // en el proximo ciclo.
                logger.LogError(ex, "Fallo el barrido de purga. Se reintentara.");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken token)
    {
        try
        {
            return await timer.WaitForNextTickAsync(token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task PurgeAsync(CancellationToken cancellationToken)
    {
        // El servicio es singleton y el DbContext es scoped: hace falta un scope
        // propio por corrida.
        await using var scope = scopeFactory.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var appConfig = scope.ServiceProvider.GetRequiredService<IAppConfigReader>();
        var eraser = scope.ServiceProvider.GetRequiredService<IFileEraser>();

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
            .Take(_settings.BatchSize * 5)
            .ToListAsync(cancellationToken);

        var expired = candidates
            .Where(f => f.DeletedAt.AddDays(f.Retention ?? globalRetention) <= now)
            .Take(_settings.BatchSize)
            .ToList();

        if (expired.Count == 0)
        {
            return;
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
    }
}
