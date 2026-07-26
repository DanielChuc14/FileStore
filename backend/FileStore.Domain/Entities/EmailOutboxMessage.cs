using FileStore.Domain.Enums;

namespace FileStore.Domain.Entities;

/// <summary>
/// Correo pendiente de entrega.
///
/// Existe para que encolar un correo sea parte de la MISMA transaccion que el
/// cambio que lo motiva. Si el alta del cliente termina en rollback, la fila del
/// correo se va con ella y nunca se envia una contraseña de una cuenta que no
/// llego a existir. Al reves tambien: entregar a Resend dentro de la transaccion
/// significaria que un rollback posterior no puede deshacer un correo ya enviado.
/// </summary>
public class EmailOutboxMessage
{
    public Guid Id { get; set; }

    public string Recipient { get; set; } = null!;
    public string Subject { get; set; } = null!;

    /// <summary>
    /// Se vacia despues de entregar. Estos cuerpos llevan contraseñas generadas y
    /// enlaces de un solo uso: conservarlos convertiria la tabla en un archivo de
    /// credenciales en claro. Queda la fila como rastro, sin el contenido.
    /// </summary>
    public string? HtmlBody { get; set; }

    public string? TextBody { get; set; }

    /// <summary>Cliente al que corresponde, si aplica. Solo para trazabilidad.</summary>
    public Guid? ClientId { get; set; }

    public EmailStatus Status { get; set; }

    public int Attempts { get; set; }

    /// <summary>Motivo del ultimo fallo, para diagnosticar sin revisar el log.</summary>
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }

    /// <summary>
    /// Cuando volver a intentar. Se separa del CreatedAt para poder espaciar los
    /// reintentos y no golpear la API del proveedor cuando esta caida.
    /// </summary>
    public DateTime? NextAttemptAt { get; set; }
}
