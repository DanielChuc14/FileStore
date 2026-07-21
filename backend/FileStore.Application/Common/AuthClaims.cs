namespace FileStore.Application.Common;

/// <summary>
/// Nombres de claims cortos. Se evitan las URIs largas de ClaimTypes porque
/// cada claim viaja en cada request: el token se mantiene chico.
/// </summary>
public static class AuthClaims
{
    public const string Role = "role";
    public const string UserId = "sub";
    public const string Email = "email";

    /// <summary>Cliente propietario. En un request con API Key es el dueño de la key.</summary>
    public const string ClientId = "client_id";

    /// <summary>API Key que autentico el request. Ausente cuando se entra por JWT.</summary>
    public const string ApiKeyId = "api_key_id";

    /// <summary>
    /// Limite por minuto ya resuelto (override de la key o default global). Se
    /// emite como claim porque el limitador de peticiones necesita leerlo de
    /// forma sincrona, y consultarlo a la base en cada request no es viable.
    /// </summary>
    public const string RateLimit = "rate_limit";
}

public static class AuthPolicies
{
    public const string SuperAdmin = nameof(SuperAdmin);
    public const string Client = nameof(Client);

    /// <summary>Endpoints de la API publica, autenticados con API Key y no con JWT.</summary>
    public const string ApiKey = nameof(ApiKey);

    /// <summary>
    /// Acceso al contenido de un cliente (/files, /folders, /trash). Admite los
    /// dos canales, API Key y JWT de rol Client, porque el explorador del panel
    /// consume los mismos endpoints que una integracion. Exige el claim
    /// client_id, lo que deja afuera al super-admin.
    /// </summary>
    public const string ClientContent = nameof(ClientContent);
}

public static class AuthSchemes
{
    public const string ApiKey = "ApiKey";

    /// <summary>Header por el que viaja la API Key.</summary>
    public const string ApiKeyHeader = "X-Api-Key";
}
