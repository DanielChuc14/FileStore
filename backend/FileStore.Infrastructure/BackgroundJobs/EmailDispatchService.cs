using FileStore.Application.Abstractions;
using FileStore.Infrastructure.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FileStore.Infrastructure.BackgroundJobs;

/// <summary>
/// Dispara la entrega de correo por temporizador. La logica vive en
/// <see cref="IEmailDispatcher"/>; aqui solo esta el ciclo.
/// </summary>
public class EmailDispatchService(
    IServiceScopeFactory scopeFactory,
    IOptions<EmailDispatchSettings> options,
    ILogger<EmailDispatchService> logger)
    : BackgroundService
{
    private readonly EmailDispatchSettings _settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            logger.LogInformation("El despachador de correo esta deshabilitado.");
            return;
        }

        var interval = TimeSpan.FromSeconds(_settings.IntervalSeconds);
        logger.LogInformation("Despachador de correo activo, cada {Interval}.", interval);

        using var timer = new PeriodicTimer(interval);

        do
        {
            try
            {
                // El servicio es singleton y el despachador (con su DbContext) es
                // scoped: hace falta un scope propio por corrida.
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IEmailDispatcher>();
                await dispatcher.DispatchPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Un fallo no puede matar el servicio: se reintenta al proximo ciclo.
                logger.LogError(ex, "Fallo el ciclo de despacho de correo. Se reintentara.");
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
