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
    /// <summary>
    /// Si lo que se encole va a poder entregarse. Lo consultan las operaciones
    /// cuyo unico canal de salida es el correo: generar una contraseña que nadie
    /// va a recibir deja una cuenta inaccesible para siempre.
    /// </summary>
    bool IsDeliveryConfigured { get; }

    void Enqueue(EmailMessage message, Guid? clientId = null);
}
