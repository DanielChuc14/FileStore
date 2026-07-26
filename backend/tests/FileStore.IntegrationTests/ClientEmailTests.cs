using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FileStore.Domain.Enums;

namespace FileStore.IntegrationTests;

/// <summary>
/// Cubre que las credenciales viajan por correo y no por la respuesta HTTP.
///
/// El punto es doble: que el correo se encole con la contraseña que realmente
/// quedo en la base, y que la respuesta al super-admin ya no la contenga. Lo
/// segundo es lo que cierra el agujero: antes habia que hacersela llegar al
/// cliente por un canal externo, normalmente un chat.
/// </summary>
[Collection("Integration")]
public class ClientEmailTests(IntegrationTestFixture fixture)
{
    private record ClientPayload(Guid Id, string Email, string Name);

    /// <summary>
    /// Extrae la contraseña del cuerpo en texto plano. Acopla el test a la
    /// plantilla a proposito: si alguien cambia el formato, este test avisa de
    /// que hay que revisar que el correo siga siendo legible.
    /// </summary>
    private static string ExtractPassword(string textBody)
    {
        var match = Regex.Match(textBody, @"Contrasena:\s*(\S+)");
        Assert.True(match.Success, $"El cuerpo no trae la contraseña:\n{textBody}");
        return match.Groups[1].Value;
    }

    private async Task<(Guid Id, string Email)> CreateRawClientAsync(HttpClient admin)
    {
        var email = $"mail-{Guid.NewGuid():N}@example.com";

        var response = await admin.PostAsJsonAsync("/admin/clients", new
        {
            email,
            name = "Cliente Correo",
            quotaBytes = 5 * 1024 * 1024
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ClientPayload>();
        return (body!.Id, email);
    }

    [Fact]
    public async Task Alta_NoDevuelveLaContrasenaEnLaRespuesta()
    {
        var adminToken = await fixture.LoginAsAdminAsync();
        using var admin = fixture.AuthenticatedClient(adminToken);

        var email = $"mail-{Guid.NewGuid():N}@example.com";
        var response = await admin.PostAsJsonAsync("/admin/clients", new
        {
            email,
            name = "Cliente Correo",
            quotaBytes = 5 * 1024 * 1024
        });

        var raw = await response.Content.ReadAsStringAsync();

        // Ni el campo ni ninguna variante: la respuesta no debe llevar nada
        // parecido a una credencial.
        Assert.DoesNotContain("assword", raw);
    }

    [Fact]
    public async Task Alta_EncolaElCorreoDeBienvenidaConLaContrasenaQueSirve()
    {
        var adminToken = await fixture.LoginAsAdminAsync();
        using var admin = fixture.AuthenticatedClient(adminToken);

        var (_, email) = await CreateRawClientAsync(admin);

        var queued = await fixture.Factory.GetQueuedEmailsAsync(email);
        var welcome = Assert.Single(queued);

        Assert.Equal(EmailStatus.Pending, welcome.Status);
        Assert.Equal(0, welcome.Attempts);
        Assert.Contains("cuenta", welcome.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(welcome.TextBody);

        // La contraseña del correo tiene que ser la que quedo hasheada en la
        // base: si se encolara otra, el cliente no podria entrar nunca.
        var password = ExtractPassword(welcome.TextBody!);
        var token = await fixture.LoginAsync(email, password);
        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public async Task Reset_DevuelveNoContentYEncolaLaContrasenaNueva()
    {
        var adminToken = await fixture.LoginAsAdminAsync();
        using var admin = fixture.AuthenticatedClient(adminToken);

        var (clientId, email) = await CreateRawClientAsync(admin);

        var reset = await admin.PostAsync($"/admin/clients/{clientId}/reset-password", null);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        var raw = await reset.Content.ReadAsStringAsync();
        Assert.DoesNotContain("assword", raw);

        // Dos correos: el de alta y el del reseteo.
        var queued = await fixture.Factory.GetQueuedEmailsAsync(email);
        Assert.Equal(2, queued.Count);

        // Y la contraseña nueva es la que vale ahora.
        var nueva = ExtractPassword(queued[0].TextBody!);
        var token = await fixture.LoginAsync(email, nueva);
        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public async Task AltaFallida_NoDejaCorreoEncolado()
    {
        var adminToken = await fixture.LoginAsAdminAsync();
        using var admin = fixture.AuthenticatedClient(adminToken);

        var (_, email) = await CreateRawClientAsync(admin);

        // Segunda alta con el mismo email: choca y termina en 409.
        var duplicate = await admin.PostAsJsonAsync("/admin/clients", new
        {
            email,
            name = "Duplicado",
            quotaBytes = 1024
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // Esta es la garantia del outbox: encolar es parte de la transaccion, asi
        // que un alta que no llego a existir no deja un correo listo para salir.
        var queued = await fixture.Factory.GetQueuedEmailsAsync(email);
        Assert.Single(queued);
    }

    [Fact]
    public async Task ElCorreoQuedaAsociadoAlCliente()
    {
        var adminToken = await fixture.LoginAsAdminAsync();
        using var admin = fixture.AuthenticatedClient(adminToken);

        var (clientId, email) = await CreateRawClientAsync(admin);

        var welcome = Assert.Single(await fixture.Factory.GetQueuedEmailsAsync(email));
        Assert.Equal(clientId, welcome.ClientId);
    }
}
