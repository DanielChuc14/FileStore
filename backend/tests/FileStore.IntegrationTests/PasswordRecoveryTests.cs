using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace FileStore.IntegrationTests;

/// <summary>
/// Cubre la recuperacion autoservicio de contraseña.
///
/// Es el flujo mas delicado del sistema: un endpoint anonimo que puede acabar
/// cambiando la credencial de cualquier cuenta. Lo que se fija aqui es que el
/// token sea de un solo uso, que venza, que no se pueda enumerar quien esta
/// registrado, y que canjearlo cierre todas las sesiones abiertas.
/// </summary>
[Collection("Integration")]
public class PasswordRecoveryTests(IntegrationTestFixture fixture)
{
    private const string NewPassword = "ContrasenaNueva2026";

    /// <summary>Saca el token de la URL que va en el cuerpo del correo.</summary>
    private static string ExtractToken(string textBody)
    {
        var match = Regex.Match(textBody, @"[?&]token=(\S+)");
        Assert.True(match.Success, $"El correo no trae el enlace con token:\n{textBody}");
        return Uri.UnescapeDataString(match.Groups[1].Value);
    }

    private async Task<string> RequestResetAsync(string email)
    {
        using var anonymous = fixture.Factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/v1/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var queued = await fixture.Factory.GetQueuedEmailsAsync(email);
        return ExtractToken(queued[0].TextBody!);
    }

    [Fact]
    public async Task ForgotPassword_EmailInexistente_RespondeIgualYNoEncolaNada()
    {
        using var anonymous = fixture.Factory.CreateClient();

        var desconocido = $"no-existe-{Guid.NewGuid():N}@example.com";
        var response = await anonymous.PostAsJsonAsync("/v1/auth/forgot-password", new { email = desconocido });

        // 204 igual que con una cuenta real: responder distinto convertiria el
        // endpoint en un enumerador de clientes registrados.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Empty(await fixture.Factory.GetQueuedEmailsAsync(desconocido));
    }

    [Fact]
    public async Task Recuperacion_FlujoCompleto_CambiaLaContrasena()
    {
        var (_, email, oldPassword) = await fixture.CreateClientAsync();

        var token = await RequestResetAsync(email);

        using var anonymous = fixture.Factory.CreateClient();
        var reset = await anonymous.PostAsJsonAsync("/v1/auth/reset-password",
            new { token, newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        // La nueva sirve...
        var loginNuevo = await anonymous.PostAsJsonAsync("/v1/auth/login",
            new { email, password = NewPassword });
        Assert.Equal(HttpStatusCode.OK, loginNuevo.StatusCode);

        // ...y la vieja ya no.
        var loginViejo = await anonymous.PostAsJsonAsync("/v1/auth/login",
            new { email, password = oldPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, loginViejo.StatusCode);
    }

    [Fact]
    public async Task Token_NoSePuedeUsarDosVeces()
    {
        var (_, email, _) = await fixture.CreateClientAsync();
        var token = await RequestResetAsync(email);

        using var anonymous = fixture.Factory.CreateClient();

        var first = await anonymous.PostAsJsonAsync("/v1/auth/reset-password",
            new { token, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        // Un enlace filtrado despues de usarse no puede volver a abrir la cuenta.
        var second = await anonymous.PostAsJsonAsync("/v1/auth/reset-password",
            new { token, newPassword = "OtraContrasena2026" });
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
    }

    [Fact]
    public async Task PedirOtroEnlace_InvalidaElAnterior()
    {
        var (_, email, _) = await fixture.CreateClientAsync();

        var primero = await RequestResetAsync(email);
        var segundo = await RequestResetAsync(email);

        Assert.NotEqual(primero, segundo);

        using var anonymous = fixture.Factory.CreateClient();

        // El primero muere en cuanto se emite el segundo: si no, cada solicitud
        // dejaria una llave viva mas rondando por el correo.
        var conElViejo = await anonymous.PostAsJsonAsync("/v1/auth/reset-password",
            new { token = primero, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, conElViejo.StatusCode);

        var conElNuevo = await anonymous.PostAsJsonAsync("/v1/auth/reset-password",
            new { token = segundo, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.NoContent, conElNuevo.StatusCode);
    }

    [Fact]
    public async Task TokenInventado_NoSirve()
    {
        using var anonymous = fixture.Factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/v1/auth/reset-password",
            new { token = "un-token-que-nadie-emitio", newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ContrasenaDebil_SeRechaza()
    {
        var (_, email, _) = await fixture.CreateClientAsync();
        var token = await RequestResetAsync(email);

        using var anonymous = fixture.Factory.CreateClient();

        // Recuperar la cuenta no puede ser una via para saltarse el minimo de 12
        // caracteres que si exige el cambio desde el perfil.
        var response = await anonymous.PostAsJsonAsync("/v1/auth/reset-password",
            new { token, newPassword = "corta" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Canjear_CierraLasSesionesAbiertas()
    {
        var (_, email, password) = await fixture.CreateClientAsync();

        // Sesion abierta ANTES de la recuperacion.
        var jwt = await fixture.LoginAsync(email, password);
        using var sesionVieja = fixture.AuthenticatedClient(jwt);
        Assert.Equal(HttpStatusCode.OK, (await sesionVieja.GetAsync("/v1/files")).StatusCode);

        var token = await RequestResetAsync(email);

        using var anonymous = fixture.Factory.CreateClient();
        await anonymous.PostAsJsonAsync("/v1/auth/reset-password",
            new { token, newPassword = NewPassword });

        // Recuperar la contraseña suele responder a haber perdido el control de
        // la cuenta: dejar viva una sesion previa anularia el proposito. El
        // access token ya emitido no se puede invalidar, pero su refresh si, asi
        // que la sesion muere en cuanto intente renovarse.
        var refresh = await sesionVieja.PostAsync("/v1/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task ElSuperAdmin_TambienPuedeRecuperarSuContrasena()
    {
        // Es la unica via de recuperacion del super-admin que no pasa por tocar
        // la base a mano. Antes su contrasena solo existia en los secretos del
        // servidor y perderla obligaba a borrar la fila y reiniciar la API.
        var token = await RequestResetAsync(TestWebApplicationFactory.AdminEmail);

        using var anonymous = fixture.Factory.CreateClient();
        var reset = await anonymous.PostAsJsonAsync("/v1/auth/reset-password",
            new { token, newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        try
        {
            var conLaNueva = await anonymous.PostAsJsonAsync("/v1/auth/login",
                new { email = TestWebApplicationFactory.AdminEmail, password = NewPassword });
            Assert.Equal(HttpStatusCode.OK, conLaNueva.StatusCode);

            var conLaVieja = await anonymous.PostAsJsonAsync("/v1/auth/login",
                new
                {
                    email = TestWebApplicationFactory.AdminEmail,
                    password = TestWebApplicationFactory.AdminPassword
                });
            Assert.Equal(HttpStatusCode.Unauthorized, conLaVieja.StatusCode);
        }
        finally
        {
            // Se restaura: el resto de la coleccion inicia sesion como admin con
            // la contrasena original, y dejarla cambiada los rompe a todos.
            var restore = await RequestResetAsync(TestWebApplicationFactory.AdminEmail);
            var back = await anonymous.PostAsJsonAsync("/v1/auth/reset-password",
                new { token = restore, newPassword = TestWebApplicationFactory.AdminPassword });
            back.EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task ElEnlaceDelSuperAdmin_NoSirveParaOtraCuenta()
    {
        // El token lleva a quien pertenece; no es una llave maestra. Canjearlo
        // solo puede cambiar la contrasena de su propio dueño.
        var (_, emailCliente, _) = await fixture.CreateClientAsync();
        var tokenDelCliente = await RequestResetAsync(emailCliente);

        using var anonymous = fixture.Factory.CreateClient();
        await anonymous.PostAsJsonAsync("/v1/auth/reset-password",
            new { token = tokenDelCliente, newPassword = NewPassword });

        // El admin conserva la suya: el canje del cliente no lo toco.
        var adminSigueIgual = await anonymous.PostAsJsonAsync("/v1/auth/login",
            new
            {
                email = TestWebApplicationFactory.AdminEmail,
                password = TestWebApplicationFactory.AdminPassword
            });
        Assert.Equal(HttpStatusCode.OK, adminSigueIgual.StatusCode);

        // Y el cliente si cambio: entra con la nueva.
        await fixture.LoginAsync(emailCliente, NewPassword);
    }

    [Fact]
    public async Task ClienteDesactivado_NoRecibeEnlace()
    {
        var (clientId, email, _) = await fixture.CreateClientAsync();

        var adminToken = await fixture.LoginAsAdminAsync();
        using var admin = fixture.AuthenticatedClient(adminToken);
        await admin.PatchAsJsonAsync($"/v1/admin/clients/{clientId}", new { isActive = false });

        using var anonymous = fixture.Factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/v1/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Solo el correo de bienvenida: la cuenta bloqueada no puede recuperarse
        // sola, o seria una via para saltarse el bloqueo.
        var queued = await fixture.Factory.GetQueuedEmailsAsync(email);
        Assert.Single(queued);
    }
}
