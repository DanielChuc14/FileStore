namespace FileStore.Application.Abstractions;

public interface IPasswordGenerator
{
    /// <summary>Genera una contraseña aleatoria criptograficamente segura.</summary>
    string Generate(int length = 16);
}
