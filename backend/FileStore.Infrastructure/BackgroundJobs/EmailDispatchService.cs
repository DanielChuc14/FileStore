using FileStore.Application.Abstractions;
using FileStore.Domain.Enums;
using FileStore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FileStore.Infrastructure.BackgroundJobs;

public class EmailDispatchSettings
{
    public const string SectionName = "EmailDispatch";

    public bool Enabled { get; set; } = true;

    /// <summary>Cada cuanto se revisa la cola.</summary>
    public int IntervalSeconds { get; set; } = 60;

    /// <summary>Correos por ciclo, para no monopolizar la base ni la API del proveedor.</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>Intentos antes de darlo por perdido.</summary>
    public int MaxAttempts { get; set; } = 5;
}

/// <summary>
/// Entrega los correos pendientes de la tabla de salida.
///
/// Asume una sola instancia de la API, que es el despliegue previsto (un VPS con
/// docker compose). Con varias instancias haria falta tomar las filas con
/// bloqueo (SELECT ... FOR UPDATE SKIP LOCKED) o dos despachadores enviarian el
/// mismo correo dos veces.
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
                await DispatchAsync(stoppingToken);
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

    private async Task DispatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<FileStoreDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        var now = DateTime.UtcNow;

        var pending = await context.EmailOutbox
            .Where(m => m.Status == EmailStatus.Pending
                && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
            .OrderBy(m => m.CreatedAt)
            .Take(_settings.BatchSize)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return;
        }

        foreach (var message in pending)
        {
            message.Attempts++;

            try
            {
                await sender.SendAsync(
                    new EmailMessage(
                        message.Recipient,
                        message.Subject,
                        message.HtmlBody ?? string.Empty,
                        message.TextBody),
                    cancellationToken);

                message.Status = EmailStatus.Sent;
                message.SentAt = DateTime.UtcNow;
                message.NextAttemptAt = null;
                message.LastError = null;

                // El cuerpo se borra en cuanto deja de hacer falta: llevaba una
                // contraseña o un enlace de un solo uso, y la tabla no es sitio
                // para conservarlos. La fila queda como rastro de la entrega.
                message.HtmlBody = null;
                message.TextBody = null;
            }
            catch (Exception ex)
            {
                message.LastError = Truncate(ex.Message, 1000);

                if (message.Attempts >= _settings.MaxAttempts)
                {
                    message.Status = EmailStatus.Failed;
                    message.NextAttemptAt = null;

                    logger.LogError(
                        ex,
                        "Se agotaron los {Attempts} intentos de enviar el correo {MessageId} a {Recipient}.",
                        message.Attempts,
                        message.Id,
                        message.Recipient);
                }
                else
                {
                    // Espera creciente: 2, 4, 8... minutos. Si el proveedor esta
                    // caido, reintentar cada minuto solo suma ruido.
                    var delay = TimeSpan.FromMinutes(Math.Pow(2, message.Attempts));
                    message.NextAttemptAt = DateTime.UtcNow.Add(delay);

                    logger.LogWarning(
                        "Fallo el intento {Attempt} de enviar el correo {MessageId}. Reintento en {Delay}.",
                        message.Attempts,
                        message.Id,
                        delay);
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
