using FileStore.Application.Abstractions;
using FileStore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FileStore.Infrastructure.Email;

/// <summary>
/// Toma los correos pendientes cuya hora de reintento ya paso, los entrega y
/// actualiza su estado.
///
/// Asume una sola instancia de la API, que es el despliegue previsto (un VPS con
/// docker compose). Con varias instancias haria falta tomar las filas con
/// bloqueo (SELECT ... FOR UPDATE SKIP LOCKED) o dos despachadores enviarian el
/// mismo correo dos veces.
/// </summary>
public class EmailDispatcher(
    IApplicationDbContext context,
    IEmailSender sender,
    IOptions<EmailDispatchSettings> options,
    ILogger<EmailDispatcher> logger)
    : IEmailDispatcher
{
    private readonly EmailDispatchSettings _settings = options.Value;

    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var pending = await context.EmailOutbox
            .Where(m => m.Status == EmailStatus.Pending
                && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
            .OrderBy(m => m.CreatedAt)
            .Take(_settings.BatchSize)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return 0;
        }

        var sent = 0;

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

                sent++;
            }
            catch (Exception ex)
            {
                // Un fallo no puede cortar el lote: los demas correos de la
                // tanda no tienen la culpa y deben intentarse igual.
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

        return sent;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
