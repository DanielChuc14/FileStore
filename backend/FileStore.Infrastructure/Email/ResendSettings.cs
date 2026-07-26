namespace FileStore.Infrastructure.Email;

public class ResendSettings
{
    public const string SectionName = "Resend";

    /// <summary>
    /// Clave de la API. Nunca va en appsettings: user-secrets en desarrollo y
    /// variable de entorno en produccion. Sin ella el envio queda desactivado.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Remitente. Su dominio tiene que estar verificado en Resend (SPF/DKIM) o
    /// la API rechaza el envio.
    /// </summary>
    public string? FromAddress { get; set; }

    public string FromName { get; set; } = "FileStore";

    /// <summary>Segundos antes de abandonar una llamada a la API.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Solo se envia de verdad si hay clave y remitente. Sin configurar, la app
    /// arranca igual y los correos se registran en el log: en desarrollo no hace
    /// falta una cuenta de Resend para trabajar.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(FromAddress);
}
