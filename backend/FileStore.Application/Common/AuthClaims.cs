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
}

public static class AuthPolicies
{
    public const string SuperAdmin = nameof(SuperAdmin);
    public const string Client = nameof(Client);

    /// <summary>Endpoints de la API publica, autenticados con API Key y no con JWT.</summary>
    public const string ApiKey = nameof(ApiKey);
}

public static class AuthSchemes
{
    public const string ApiKey = "ApiKey";

    /// <summary>Header por el que viaja la API Key.</summary>
    public const string ApiKeyHeader = "X-Api-Key";
}
