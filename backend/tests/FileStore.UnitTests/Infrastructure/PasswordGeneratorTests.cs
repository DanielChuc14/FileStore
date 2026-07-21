using FileStore.Infrastructure.Authentication;

namespace FileStore.UnitTests.Infrastructure;

public class PasswordGeneratorTests
{
    private readonly PasswordGenerator _generator = new();

    [Theory]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(24)]
    public void Generate_RespetaLaLongitudPedida(int length)
    {
        Assert.Equal(length, _generator.Generate(length).Length);
    }

    [Fact]
    public void Generate_MenorAlMinimo_Lanza()
    {
        // Menos de 12 caracteres no es una contraseña aceptable: el generador
        // no debe producir algo debil ni siquiera si se lo piden.
        Assert.Throws<ArgumentOutOfRangeException>(() => _generator.Generate(8));
    }

    [Fact]
    public void Generate_SinCaracteresAmbiguos()
    {
        // La contraseña se transcribe a mano desde el panel. 0/O y 1/l/I se
        // confunden y generan soporte innecesario, asi que no deben aparecer.
        var ambiguos = new[] { '0', 'O', '1', 'l', 'I' };

        // Se generan muchas para que el test no dependa de una tirada afortunada.
        for (var i = 0; i < 200; i++)
        {
            var password = _generator.Generate(24);
            Assert.DoesNotContain(password, c => ambiguos.Contains(c));
        }
    }

    [Fact]
    public void Generate_ProduceValoresDistintos()
    {
        // Dos contraseñas seguidas no pueden coincidir: si coincidieran, el
        // generador no seria aleatorio.
        var generadas = Enumerable.Range(0, 100)
            .Select(_ => _generator.Generate(16))
            .ToHashSet();

        Assert.Equal(100, generadas.Count);
    }
}
