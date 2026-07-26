using System.Security.Cryptography;
using System.Text;
using FileStore.Application.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace FileStore.Infrastructure.Authentication;

/// <summary>
/// Mismo criterio que el refresh token: 32 bytes de entropia criptografica,
/// Base64Url para que viaje limpio en una URL, y SHA-256 sin salt para poder
/// buscarlo por indice. El token ya es aleatorio de 256 bits, asi que no hay
/// nada que un salt protegiera contra fuerza bruta o diccionario.
/// </summary>
public class SecureTokenGenerator : ISecureTokenGenerator
{
    public (string Token, string TokenHash) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Base64UrlEncoder.Encode(bytes);

        return (token, Hash(token));
    }

    public string Hash(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
