using System.Security.Cryptography;
using System.Text;
using FileStore.Application.Abstractions;
using FileStore.Application.Common;
using FileStore.Domain.Enums;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FileStore.Infrastructure.Authentication;

public class JwtTokenGenerator(IOptions<JwtSettings> options) : IJwtTokenGenerator
{
    private readonly JwtSettings _settings = options.Value;

    public (string Token, DateTime ExpiresAt) GenerateAccessToken(
        Guid userId,
        string email,
        UserType userType)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(_settings.AccessTokenMinutes);

        var claims = new Dictionary<string, object>
        {
            [AuthClaims.UserId] = userId.ToString(),
            [AuthClaims.Email] = email,
            [AuthClaims.Role] = userType.ToString(),
            [JwtRegisteredClaimNames.Jti] = Guid.CreateVersion7().ToString()
        };

        // Solo un cliente es dueño de contenido. El super-admin no lleva este
        // claim, y por eso la politica de /files y /folders lo deja afuera.
        if (userType == UserType.Client)
        {
            claims[AuthClaims.ClientId] = userId.ToString();
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            Expires = expiresAt,
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret)),
                SecurityAlgorithms.HmacSha256)
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expiresAt);
    }

    public (string Token, string TokenHash, DateTime ExpiresAt) GenerateRefreshToken()
    {
        // 32 bytes de entropia criptografica. Base64Url para que viaje limpio
        // en una cookie sin necesidad de escaping.
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Base64UrlEncoder.Encode(bytes);

        return (token, HashRefreshToken(token), DateTime.UtcNow.AddDays(_settings.RefreshTokenDays));
    }

    public string HashRefreshToken(string token)
    {
        // SHA-256 sin salt a proposito: el token ya es aleatorio de 256 bits, no
        // es adivinable por fuerza bruta ni por diccionario. Un hash con salt
        // impediria buscarlo por indice, que es justo lo que necesita el refresh.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
