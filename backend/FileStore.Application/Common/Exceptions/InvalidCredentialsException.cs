namespace FileStore.Application.Common.Exceptions;

/// <summary>
/// Credenciales invalidas o sesion no valida. El mensaje es deliberadamente
/// generico: distinguir "email inexistente" de "contraseña incorrecta" le
/// permitiria a un atacante enumerar cuentas registradas.
/// </summary>
public class InvalidCredentialsException(string message = "Invalid credentials.")
    : Exception(message);
