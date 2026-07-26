namespace FileStore.Application.Abstractions;

/// <summary>
/// Genera tokens opacos de un solo uso, como el de recuperacion de contraseña.
///
/// Se separa de <see cref="IJwtTokenGenerator"/> porque no tiene nada que ver
/// con JWT ni con sesiones: es un secreto aleatorio que viaja por correo y se
/// canjea una vez.
/// </summary>
public interface ISecureTokenGenerator
{
    /// <summary>
    /// Devuelve el valor en claro (que viaja al usuario y no se persiste) y su
    /// hash (lo unico que se guarda).
    /// </summary>
    (string Token, string TokenHash) Generate();

    /// <summary>Hashea un token recibido para poder buscarlo por indice.</summary>
    string Hash(string token);
}
