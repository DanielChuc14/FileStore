namespace FileStore.Domain.Enums;

/// <summary>
/// Principal que puede iniciar sesion en el panel. Separado de ActorType porque
/// una API Key nunca hace login: se autentica request a request, sin sesion.
/// </summary>
public enum UserType
{
    Client = 1,
    SuperAdmin = 2
}
