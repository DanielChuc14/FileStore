namespace FileStore.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>
    /// Verifica una contraseña contra su hash. NeedsRehash indica que el hash es
    /// valido pero se genero con parametros viejos: conviene regenerarlo.
    /// </summary>
    (bool IsValid, bool NeedsRehash) Verify(string hash, string password);
}
