using System.Net;
using System.Net.Http.Json;

namespace FileStore.IntegrationTests;

/// <summary>
/// Cubre que las operaciones cuyo unico canal de salida es el correo se nieguen
/// a ejecutarse cuando no hay envio configurado.
///
/// Sin estas guardas, dar de alta un cliente con el correo apagado genera una
/// contraseña que no devuelve la API, no registra el log y no entrega nadie: la
/// cuenta queda inaccesible para siempre salvo escribiendo un hash a mano en la
/// base. El reseteo es peor, porque ademas destruye una contraseña que servia.
///
/// Es un escenario real, no teorico: entre que se despliega y se termina de
/// verificar el dominio del remitente, el sistema vive exactamente asi.
/// </summary>
[Collection("Integration")]
public class EmailNotConfiguredTests(IntegrationTestFixture fixture)
{
    private record ClientPayload(Guid Id, string Email, string Name);

    [Fact]
    public async Task Alta_SinCorreoConfigurado_Devuelve503YNoCreaElCliente()
    {
        var adminToken = await fixture.LoginAsAdminAsync();
        using var admin = fixture.AuthenticatedClient(adminToken);

        var email = $"sin-correo-{Guid.NewGuid():N}@example.com";

        await fixture.Factory.WithEmailDeliveryDisabledAsync(async () =>
        {
            var response = await admin.PostAsJsonAsync("/v1/admin/clients", new
            {
                email,
                name = "Cliente Sin Correo",
                quotaBytes = 1024 * 1024
            });

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

            // El mensaje tiene que decir que hacer, no solo que fallo: quien lo
            // lee es quien administra el servidor.
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Resend", body);
        });

        // Y no queda ninguna cuenta a medias.
        var listado = await admin.GetFromJsonAsync<PagedClients>($"/v1/admin/clients?search={email}");
        Assert.Empty(listado!.Items);
    }

    [Fact]
    public async Task Reset_SinCorreoConfigurado_NoDestruyeLaContrasenaActual()
    {
        var (clientId, email, password) = await fixture.CreateClientAsync();

        await fixture.Factory.WithEmailDeliveryDisabledAsync(async () =>
        {
            var adminToken = await fixture.LoginAsAdminAsync();
            using var admin = fixture.AuthenticatedClient(adminToken);

            var response = await admin.PostAsync($"/v1/admin/clients/{clientId}/reset-password", null);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        });

        // Lo que de verdad importa: la contraseña que el cliente ya tenia sigue
        // sirviendo. Un reseteo a medias lo habria dejado fuera de su cuenta.
        var token = await fixture.LoginAsync(email, password);
        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public async Task Recuperacion_SinCorreoConfigurado_Devuelve503()
    {
        var (_, email, _) = await fixture.CreateClientAsync();

        await fixture.Factory.WithEmailDeliveryDisabledAsync(async () =>
        {
            using var anonymous = fixture.Factory.CreateClient();

            var response = await anonymous.PostAsJsonAsync("/v1/auth/forgot-password", new { email });

            // Aqui no se destruye nada, pero devolver 204 dejaria al usuario
            // esperando indefinidamente un correo que no iba a salir.
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        });
    }

    [Fact]
    public async Task Recuperacion_SinCorreoConfigurado_SigueSinRevelarQueCuentasExisten()
    {
        var (_, existente, _) = await fixture.CreateClientAsync();
        var inexistente = $"nadie-{Guid.NewGuid():N}@example.com";

        await fixture.Factory.WithEmailDeliveryDisabledAsync(async () =>
        {
            using var anonymous = fixture.Factory.CreateClient();

            var conCuenta = await anonymous.PostAsJsonAsync("/v1/auth/forgot-password", new { email = existente });
            var sinCuenta = await anonymous.PostAsJsonAsync("/v1/auth/forgot-password", new { email = inexistente });

            // La guarda se evalua antes de mirar si la cuenta existe, asi que la
            // respuesta es identica en ambos casos. Si dependiera de la cuenta,
            // arreglar un problema habria abierto otro: un enumerador de clientes.
            Assert.Equal(conCuenta.StatusCode, sinCuenta.StatusCode);
        });
    }

    [Fact]
    public async Task OperacionesQueNoDependenDelCorreo_SiguenFuncionando()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var jwt = await fixture.LoginAsync(email, password);

        await fixture.Factory.WithEmailDeliveryDisabledAsync(async () =>
        {
            using var client = fixture.AuthenticatedClient(jwt);

            // Crear una API Key manda un aviso, pero ese aviso es informativo:
            // perderlo no rompe nada. Bloquear la operacion seria pasarse.
            var created = await client.PostAsJsonAsync("/v1/me/api-keys", new { name = "sin-correo" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            var files = await client.GetAsync("/v1/files");
            Assert.Equal(HttpStatusCode.OK, files.StatusCode);
        });
    }

    private record PagedClients(List<ClientPayload> Items);
}
