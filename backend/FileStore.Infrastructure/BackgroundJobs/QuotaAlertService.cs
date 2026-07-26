using FileStore.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FileStore.Infrastructure.BackgroundJobs;

public class QuotaAlertSettings
{
    public const string SectionName = "QuotaAlerts";

    public bool Enabled { get; set; } = true;

    /// <summary>Cada cuanto se revisa el consumo de todos los clientes.</summary>
    public int IntervalMinutes { get; set; } = 60;
}

/// <summary>
/// Dispara la revision de cuotas por temporizador. La logica vive en
/// <see cref="IQuotaAlerter"/>; aqui solo esta el ciclo.
///
/// Es un job y no un gancho en la subida a proposito: el consumo tambien cambia
/// al borrar, al restaurar de la papelera y al purgar. Enganchado solo al upload,
/// un cliente que sube hasta el 79% y despues ve crecer su papelera nunca
/// recibiria el aviso.
/// </summary>
public class QuotaAlertService(
    IServiceScopeFactory scopeFactory,
    IOptions<QuotaAlertSettings> options,
    ILogger<QuotaAlertService> logger)
    : BackgroundService
{
    private readonly QuotaAlertSettings _settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            logger.LogInformation("Los avisos de cuota estan deshabilitados.");
            return;
        }

        var interval = TimeSpan.FromMinutes(_settings.IntervalMinutes);
        logger.LogInformation("Avisos de cuota activos, cada {Interval}.", interval);

        using var timer = new PeriodicTimer(interval);

        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var alerter = scope.ServiceProvider.GetRequiredService<IQuotaAlerter>();
                await alerter.CheckAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Un fallo no puede matar el servicio: se reintenta al proximo ciclo.
                logger.LogError(ex, "Fallo la revision de cuotas. Se reintentara.");
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
}
