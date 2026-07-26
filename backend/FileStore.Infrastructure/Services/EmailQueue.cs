using FileStore.Application.Abstractions;
using FileStore.Domain.Entities;
using FileStore.Domain.Enums;

namespace FileStore.Infrastructure.Services;

/// <summary>
/// Deja el correo en la tabla de salida. No llama a SaveChanges, igual que
/// <see cref="AuditLogger"/>: quien encola decide cuando se confirma todo junto.
/// </summary>
public class EmailQueue(IApplicationDbContext context, IEmailSender sender) : IEmailQueue
{
    public bool IsDeliveryConfigured => sender.IsConfigured;

    public void Enqueue(EmailMessage message, Guid? clientId = null)
    {
        context.EmailOutbox.Add(new EmailOutboxMessage
        {
            Id = Guid.CreateVersion7(),
            Recipient = message.To,
            Subject = message.Subject,
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody,
            ClientId = clientId,
            Status = EmailStatus.Pending,
            Attempts = 0,
            CreatedAt = DateTime.UtcNow,

            // Disponible de inmediato: el despachador lo toma en su proximo ciclo.
            NextAttemptAt = DateTime.UtcNow
        });
    }
}
