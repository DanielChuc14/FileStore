using FileStore.Application.Abstractions;
using FileStore.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace FileStore.Infrastructure.Authentication;

/// <summary>
/// Envuelve PasswordHasher de ASP.NET Identity (PBKDF2-SHA256, 100k iteraciones,
/// salt por usuario). Se adapta detras de IPasswordHasher para que Application no
/// dependa de Identity y para poder cambiar el algoritmo sin tocar los handlers.
/// </summary>
public class IdentityPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<object> _hasher = new();
    private static readonly object Dummy = new();

    public string Hash(string password) => _hasher.HashPassword(Dummy, password);

    public (bool IsValid, bool NeedsRehash) Verify(string hash, string password)
    {
        var result = _hasher.VerifyHashedPassword(Dummy, hash, password);

        return result switch
        {
            PasswordVerificationResult.Success => (true, false),
            PasswordVerificationResult.SuccessRehashNeeded => (true, true),
            _ => (false, false)
        };
    }
}
