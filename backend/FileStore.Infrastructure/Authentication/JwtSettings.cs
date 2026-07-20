namespace FileStore.Infrastructure.Authentication;

public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = null!;
    public string Issuer { get; set; } = "FileStore";
    public string Audience { get; set; } = "FileStore";

    /// <summary>Corto a proposito: si el token se filtra, la ventana de abuso es minima.</summary>
    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 7;
}
