using FileStore.Infrastructure.Authentication;

namespace FileStore.UnitTests.Infrastructure;

public class PasswordHasherTests
{
    private readonly IdentityPasswordHasher _hasher = new();

    [Fact]
    public void HashYVerify_ContrasenaCorrecta_EsValida()
    {
        var hash = _hasher.Hash("MiContrasenaSegura2026");

        var (isValid, _) = _hasher.Verify(hash, "MiContrasenaSegura2026");
        Assert.True(isValid);
    }

    [Fact]
    public void Verify_ContrasenaIncorrecta_NoEsValida()
    {
        var hash = _hasher.Hash("MiContrasenaSegura2026");

        var (isValid, _) = _hasher.Verify(hash, "OtraDistinta");
        Assert.False(isValid);
    }

    [Fact]
    public void Hash_NoGuardaLaContrasenaEnClaro()
    {
        var password = "MiContrasenaSegura2026";
        var hash = _hasher.Hash(password);

        Assert.DoesNotContain(password, hash);
    }

    [Fact]
    public void Hash_MismoTextoDosVeces_ProduceHashesDistintos()
    {
        // El salt es por hash: dos hashes de la misma contraseña deben diferir.
        // Si fueran iguales, dos usuarios con la misma clave se delatarian.
        var a = _hasher.Hash("igual");
        var b = _hasher.Hash("igual");

        Assert.NotEqual(a, b);

        // Aun asi ambos deben verificar la contraseña original.
        Assert.True(_hasher.Verify(a, "igual").IsValid);
        Assert.True(_hasher.Verify(b, "igual").IsValid);
    }
}
