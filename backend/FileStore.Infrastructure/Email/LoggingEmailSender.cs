using FileStore.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace FileStore.Infrastructure.Email;

/// <summary>
/// Sustituto cuando Resend no esta configurado: deja constancia en el log en vez
/// de enviar. Permite desarrollar el flujo completo sin cuenta de correo, y
/// evita que un despliegue mal configurado falle en silencio (queda el warning).
///
/// Igual que el sender real, NO registra el cuerpo: es justo el que llevaria la
/// contraseña generada.
/// </summary>
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public bool IsConfigured => false;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogWarning(
            "Envio de correo omitido (Resend no configurado). Destinatario {Recipient}, asunto {Subject}.",
            message.To,
            message.Subject);

        return Task.CompletedTask;
    }
}
