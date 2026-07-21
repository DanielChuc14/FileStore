using FileStore.Infrastructure.Authentication;

namespace FileStore.UnitTests.Infrastructure;

public class ApiKeyGeneratorTests
{
    private readonly ApiKeyGenerator _generator = new();

    [Fact]
    public void Generate_FormatoEsperado()
    {
        var key = _generator.Generate();

        // fs_live_XXXXXXXX.SECRETO: el prefijo publico y el secreto separados
        // por un punto.
        Assert.StartsWith("fs_live_", key.Value);
        Assert.Contains('.', key.Value);
        Assert.Equal(16, key.Prefix.Length);
        Assert.StartsWith(key.Prefix, key.Value);
    }

    [Fact]
    public void Generate_ElHashEsSha256Hex()
    {
        var key = _generator.Generate();

        // SHA-256 en hexadecimal son 64 caracteres. El hash debe corresponder
        // al valor completo, para poder verificarlo al autenticar.
        Assert.Equal(64, key.Hash.Length);
        Assert.Equal(key.Hash, _generator.Hash(key.Value));
    }

    [Fact]
    public void Generate_NoPersisteElSecretoEnElHash()
    {
        var key = _generator.Generate();

        // El hash no debe contener el valor: si lo contuviera, guardar el hash
        // filtraria la key.
        Assert.DoesNotContain(key.Value, key.Hash);
    }

    [Fact]
    public void Generate_ValoresUnicos()
    {
        var keys = Enumerable.Range(0, 100).Select(_ => _generator.Generate()).ToList();

        Assert.Equal(100, keys.Select(k => k.Value).Distinct().Count());
        Assert.Equal(100, keys.Select(k => k.Prefix).Distinct().Count());
    }

    [Fact]
    public void Hash_EsDeterministaParaElMismoValor()
    {
        // Autenticar depende de que el mismo valor produzca siempre el mismo
        // hash: se busca la key por hash en la base.
        var key = _generator.Generate();
        Assert.Equal(_generator.Hash(key.Value), _generator.Hash(key.Value));
    }

    [Fact]
    public void ExtractPrefix_KeyValida_DevuelveElPrefijo()
    {
        var key = _generator.Generate();
        Assert.Equal(key.Prefix, _generator.ExtractPrefix(key.Value));
    }

    [Theory]
    [InlineData("no-es-una-key")]
    [InlineData("fs_live_sinpunto")]
    [InlineData("")]
    [InlineData("otro_prefijo.secreto")]
    public void ExtractPrefix_KeyMalformada_DevuelveNull(string presented)
    {
        // Si la key no tiene el formato esperado, el handler debe poder
        // rechazarla antes de tocar la base.
        Assert.Null(_generator.ExtractPrefix(presented));
    }
}
