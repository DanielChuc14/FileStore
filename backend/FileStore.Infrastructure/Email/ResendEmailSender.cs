using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FileStore.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FileStore.Infrastructure.Email;

/// <summary>Fallo al entregar a Resend. La distingue el reintento del outbox.</summary>
public class EmailSendException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Envia por la API REST de Resend.
///
/// Se llama al endpoint con un HttpClient tipado en vez de usar un SDK a
/// proposito: enviar es un unico POST con cuatro campos, y este proyecto ya se
/// llevo tres sorpresas de licencia con dependencias que parecian inocuas
/// (MediatR, ApexCharts, FluentAssertions). Una dependencia menos que auditar.
/// </summary>
public class ResendEmailSender(
    HttpClient httpClient,
    IOptions<ResendSettings> options,
    ILogger<ResendEmailSender> logger)
    : IEmailSender
{
    private readonly ResendSettings _settings = options.Value;

    // Solo se registra esta implementacion cuando la configuracion esta completa.
    public bool IsConfigured => true;

    private record ResendRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("text")] string? Text);

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var from = string.IsNullOrWhiteSpace(_settings.FromName)
            ? _settings.FromAddress!
            : $"{_settings.FromName} <{_settings.FromAddress}>";

        var payload = new ResendRequest(
            from,
            [message.To],
            message.Subject,
            message.HtmlBody,
            message.TextBody);

        HttpResponseMessage response;

        try
        {
            response = await httpClient.PostAsJsonAsync("emails", payload, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Se envuelve para que quien reintente distinga un fallo de entrega
            // de un error de programacion.
            throw new EmailSendException("No se pudo contactar con la API de Resend.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            // El cuerpo del error de Resend describe el problema (dominio sin
            // verificar, destinatario invalido) y no contiene el mensaje enviado,
            // asi que es seguro registrarlo.
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);

            throw new EmailSendException(
                $"Resend rechazo el envio con {(int)response.StatusCode}: {detail}");
        }

        // Se registra el destinatario y el asunto, nunca el cuerpo: estos correos
        // transportan contraseñas generadas y enlaces de un solo uso, y el log no
        // es un sitio donde deban terminar.
        logger.LogInformation(
            "Correo enviado a {Recipient} con asunto {Subject}.",
            message.To,
            message.Subject);
    }
}
