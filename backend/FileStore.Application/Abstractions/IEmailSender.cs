namespace FileStore.Application.Abstractions;

/// <summary>
/// Un correo listo para enviar. El cuerpo se arma en la capa que lo pide; el
/// sender no sabe de plantillas ni de idiomas, solo entrega.
/// </summary>
/// <param name="TextBody">
/// Alternativa en texto plano. Opcional, pero conviene mandarla: sin ella
/// algunos clientes de correo y filtros anti-spam penalizan el mensaje.
/// </param>
public record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string? TextBody = null);

/// <summary>
/// Envio de correo transaccional.
///
/// Igual que <see cref="IStorageService"/>, la abstraccion vive en Application y
/// la implementacion concreta en Infrastructure: cambiar de proveedor no deberia
/// tocar ningun handler.
///
/// Ojo con donde se llama: enviar dentro de una transaccion abierta significa
/// que un rollback posterior deja un correo ya entregado que no se puede
/// deshacer. Para contenido sensible (credenciales) eso es especialmente malo.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Si hay un proveedor real detras. En falso, encolar un correo equivale a
    /// tirarlo: quien dependa de que llegue (una contraseña generada, sobre todo)
    /// tiene que comprobarlo ANTES de hacer el cambio, no despues.
    /// </summary>
    bool IsConfigured { get; }

    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
