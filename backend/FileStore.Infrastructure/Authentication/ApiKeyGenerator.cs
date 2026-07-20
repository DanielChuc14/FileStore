using System.Security.Cryptography;
using System.Text;
using FileStore.Application.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace FileStore.Infrastructure.Authentication;

/// <summary>
/// Formato: fs_live_XXXXXXXX.SSSSSSSS...
///          |--- prefijo ---| |- secreto -|
///
/// El prefijo es publico y se guarda en claro: identifica la key en el panel y
/// permite buscarla por indice. El secreto solo existe en el momento de crearla;
/// despues queda unicamente su hash.
/// </summary>
public class ApiKeyGenerator : IApiKeyGenerator
{
    private const string Scheme = "fs_live_";

    /// <summary>Longitud del prefijo completo, incluido el esquema.</summary>
    private const int PrefixLength = 16;

    private const char Separator = '.';

    public GeneratedApiKey Generate()
    {
        // 6 bytes -> 8 caracteres base64url, que completan los 16 del prefijo.
        var prefixSuffix = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(6));
        var prefix = Scheme + prefixSuffix[..(PrefixLength - Scheme.Length)];

        // 32 bytes de entropia para la parte secreta.
        var secret = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var value = prefix + Separator + secret;

        return new GeneratedApiKey(value, prefix, Hash(value));
    }

    public string? ExtractPrefix(string presentedKey)
    {
        if (string.IsNullOrWhiteSpace(presentedKey) ||
            !presentedKey.StartsWith(Scheme, StringComparison.Ordinal) ||
            presentedKey.Length <= PrefixLength ||
            presentedKey[PrefixLength] != Separator)
        {
            return null;
        }

        return presentedKey[..PrefixLength];
    }

    public string Hash(string presentedKey)
    {
        // SHA-256 sin salt, igual que los refresh tokens: el valor ya tiene 256
        // bits de entropia, de modo que no hay diccionario ni fuerza bruta viable.
        // Un hash lento tipo PBKDF2 penalizaria cada request de la API sin
        // agregar seguridad real.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(presentedKey));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
