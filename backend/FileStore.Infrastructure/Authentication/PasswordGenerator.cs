using System.Security.Cryptography;
using FileStore.Application.Abstractions;

namespace FileStore.Infrastructure.Authentication;

public class PasswordGenerator : IPasswordGenerator
{
    // Sin caracteres ambiguos (0/O, 1/l/I): la contraseña se transcribe a mano
    // desde el panel, y confundirlos genera soporte innecesario.
    private const string Alphabet =
        "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public string Generate(int length = 16)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 12);

        // GetString evita el sesgo del modulo que aparece al mapear bytes
        // aleatorios sobre un alfabeto cuyo tamaño no es potencia de dos.
        return RandomNumberGenerator.GetString(Alphabet, length);
    }
}
