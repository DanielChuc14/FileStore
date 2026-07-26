using FileStore.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace FileStore.Infrastructure.Services;

public class AppSettings
{
    public const string SectionName = "App";

    /// <summary>
    /// Raiz publica del panel. En produccion es el dominio detras de Nginx; el
    /// default sirve para desarrollo con ng serve.
    /// </summary>
    public string PanelUrl { get; set; } = "http://localhost:4200";
}

public class AppUrlProvider(IOptions<AppSettings> options) : IAppUrlProvider
{
    // Se normaliza una vez: concatenar rutas sobre una raiz con barra final
    // produce dobles barras, que algunos proxies tratan como otra ruta.
    public string PanelUrl { get; } = options.Value.PanelUrl.TrimEnd('/');

    public string BuildPasswordResetUrl(string token) =>
        $"{PanelUrl}/reset-password?token={Uri.EscapeDataString(token)}";
}
