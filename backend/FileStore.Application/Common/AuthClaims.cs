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
}

public static class AuthPolicies
{
    public const string SuperAdmin = nameof(SuperAdmin);
    public const string Client = nameof(Client);
}
