namespace FileStore.Application.Abstractions;

/// <summary>
/// Encola un correo para que lo entregue el despachador en segundo plano.
///
/// Se usa esto y no <see cref="IEmailSender"/> directamente desde los handlers:
/// igual que <see cref="IAuditLogger"/>, solo agrega la fila al contexto sin
/// guardar, asi que el correo se confirma en la misma transaccion que el cambio
/// que lo motiva. Un rollback se lleva el correo con el.
/// </summary>
public interface IEmailQueue
{
    void Enqueue(EmailMessage message, Guid? clientId = null);
}
