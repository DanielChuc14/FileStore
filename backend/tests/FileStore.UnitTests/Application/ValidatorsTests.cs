using FileStore.Application.Features.Clients.Create;
using FileStore.Application.Features.Config;
using FileStore.Application.Features.Profile;

namespace FileStore.UnitTests.Application;

/// <summary>
/// Los validadores de FluentValidation son objetos puros: se instancian y se
/// ejecutan sin base ni red. Testearlos aca es barato y documenta exactamente
/// que entradas acepta y rechaza cada comando.
/// </summary>
public class ValidatorsTests
{
    public class CreateClient
    {
        private readonly CreateClientCommandValidator _validator = new();

        [Fact]
        public void ComandoValido_Pasa()
        {
            var command = new CreateClientCommand("cliente@example.com", "Cliente", 1048576, null, null);
            Assert.True(_validator.Validate(command).IsValid);
        }

        [Theory]
        [InlineData("no-es-email")]
        [InlineData("")]
        public void EmailInvalido_Falla(string email)
        {
            var command = new CreateClientCommand(email, "Cliente", 1048576, null, null);
            Assert.False(_validator.Validate(command).IsValid);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-100)]
        public void CuotaNoPositiva_Falla(long quota)
        {
            // Una cuota de cero o negativa no tiene sentido: el cliente no
            // podria subir nada, o el calculo quedaria roto.
            var command = new CreateClientCommand("c@example.com", "Cliente", quota, null, null);
            Assert.False(_validator.Validate(command).IsValid);
        }

        [Fact]
        public void RetencionFueraDeRango_Falla()
        {
            var command = new CreateClientCommand("c@example.com", "Cliente", 1024, 0, null);
            Assert.False(_validator.Validate(command).IsValid);
        }
    }

    public class ChangePassword
    {
        private readonly ChangePasswordCommandValidator _validator = new();

        [Fact]
        public void ComandoValido_Pasa()
        {
            var command = new ChangePasswordCommand("actual123", "ContrasenaNueva2026", null);
            Assert.True(_validator.Validate(command).IsValid);
        }

        [Fact]
        public void NuevaMuyCorta_Falla()
        {
            // El minimo son 12 caracteres; menos no es aceptable.
            var command = new ChangePasswordCommand("actual123", "corta", null);
            Assert.False(_validator.Validate(command).IsValid);
        }

        [Fact]
        public void NuevaIgualALaActual_Falla()
        {
            // Cambiar la contraseña por la misma no cambia nada; se rechaza.
            var command = new ChangePasswordCommand("MismaClave2026", "MismaClave2026", null);
            Assert.False(_validator.Validate(command).IsValid);
        }
    }

    public class UpdateConfig
    {
        private readonly UpdateConfigCommandValidator _validator = new();

        [Fact]
        public void RetencionExcesiva_Falla()
        {
            // El tope es 365 dias: 9999 se rechaza (fue el caso que verificamos
            // a mano en el panel).
            var command = new UpdateConfigCommand(null, 9999, null);
            Assert.False(_validator.Validate(command).IsValid);
        }

        [Fact]
        public void SoloUnCampo_Pasa()
        {
            // El PATCH es parcial: mandar solo un campo valido debe pasar.
            var command = new UpdateConfigCommand(null, 15, null);
            Assert.True(_validator.Validate(command).IsValid);
        }

        [Fact]
        public void TodoNulo_Pasa()
        {
            // No cambiar nada es valido: el handler simplemente no toca nada.
            var command = new UpdateConfigCommand(null, null, null);
            Assert.True(_validator.Validate(command).IsValid);
        }
    }
}
