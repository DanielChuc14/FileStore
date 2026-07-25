using FileStore.Application.Abstractions;
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
        // El servicio es singleton y el purger (con su DbContext) es scoped: hace
        // falta un scope propio por corrida.
        await using var scope = scopeFactory.CreateAsyncScope();

        var purger = scope.ServiceProvider.GetRequiredService<ITrashPurger>();
        await purger.PurgeExpiredAsync(_settings.BatchSize, cancellationToken);
    }
}
