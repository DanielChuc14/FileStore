namespace FileStore.Application.Abstractions;

/// <summary>
/// Direcciones publicas de la aplicacion. Los correos necesitan enlazar al panel
/// y Application no puede leer la configuracion por si misma.
/// </summary>
public interface IAppUrlProvider
{
    /// <summary>Raiz del panel, sin barra final. Ej: https://files.midominio.com</summary>
    string PanelUrl { get; }

    /// <summary>Enlace para establecer o recuperar la contraseña con un token.</summary>
    string BuildPasswordResetUrl(string token);
}
