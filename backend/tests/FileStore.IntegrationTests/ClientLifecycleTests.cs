using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace FileStore.IntegrationTests;

/// <summary>
/// Cubre que dar de baja o desactivar a un cliente le corta el acceso de
/// inmediato, por los dos canales.
///
/// El caso delicado es el JWT: a diferencia de la API Key, no consulta la base
/// en cada request, asi que un access token emitido ANTES de la baja seguiria
/// siendo criptograficamente valido. Sin la comprobacion del pipeline, el
/// cliente dado de baja podia seguir listando y descargando hasta que el token
/// expirara. Estos tests fijan que no.
/// </summary>
[Collection("Integration")]
public class ClientLifecycleTests(IntegrationTestFixture fixture)
{
    private record UploadedFile(Guid Id, string OriginalName);
    private record CreateKeyResult(string Value);

    private static MultipartFormDataContent FileForm(string name, string content)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "file", name);
        return form;
    }

    private async Task SetActiveAsync(Guid clientId, bool isActive)
    {
        var adminToken = await fixture.LoginAsAdminAsync();
        using var admin = fixture.AuthenticatedClient(adminToken);

        var response = await admin.PatchAsJsonAsync(
            $"/admin/clients/{clientId}", new { isActive });

        response.EnsureSuccessStatusCode();
    }

    private async Task DeleteClientAsync(Guid clientId)
    {
        var adminToken = await fixture.LoginAsAdminAsync();
        using var admin = fixture.AuthenticatedClient(adminToken);

        var response = await admin.DeleteAsync($"/admin/clients/{clientId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ClienteDesactivado_ConAccessTokenVigente_NoPuedeListarNiDescargar()
    {
        var (clientId, email, password) = await fixture.CreateClientAsync();
        var jwt = await fixture.LoginAsync(email, password);

        // El token se obtiene y el archivo se sube ANTES de la baja: es
        // exactamente el escenario del cliente que ya tenia sesion abierta.
        using var client = fixture.AuthenticatedClient(jwt);
        using var form = FileForm("antes-de-la-baja.txt", "contenido");
        var upload = await client.PostAsync("/files", form);
        upload.EnsureSuccessStatusCode();
        var file = await upload.Content.ReadFromJsonAsync<UploadedFile>();

        await SetActiveAsync(clientId, isActive: false);

        // Mismo token, que sigue sin expirar.
        var list = await client.GetAsync("/files");
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);

        var download = await client.GetAsync($"/files/{file!.Id}");
        Assert.Equal(HttpStatusCode.Unauthorized, download.StatusCode);
    }

    [Fact]
    public async Task ClienteDesactivado_ConAccessTokenVigente_NoPuedeBorrarNiSubir()
    {
        var (clientId, email, password) = await fixture.CreateClientAsync();
        var jwt = await fixture.LoginAsync(email, password);

        using var client = fixture.AuthenticatedClient(jwt);
        using var form = FileForm("antes-de-la-baja.txt", "contenido");
        var upload = await client.PostAsync("/files", form);
        var file = await upload.Content.ReadFromJsonAsync<UploadedFile>();

        await SetActiveAsync(clientId, isActive: false);

        // Las mutaciones tambien se cortan: si solo se bloqueara la lectura, un
        // cliente dado de baja podria seguir consumiendo cuota o destruyendo
        // datos durante la ventana del token.
        using var second = FileForm("despues-de-la-baja.txt", "contenido");
        var blockedUpload = await client.PostAsync("/files", second);
        Assert.Equal(HttpStatusCode.Unauthorized, blockedUpload.StatusCode);

        var delete = await client.DeleteAsync($"/files/{file!.Id}");
        Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);
    }

    [Fact]
    public async Task ClienteReactivado_VuelveAOperarConElMismoToken()
    {
        var (clientId, email, password) = await fixture.CreateClientAsync();
        var jwt = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(jwt);

        await SetActiveAsync(clientId, isActive: false);
        var blocked = await client.GetAsync("/files");
        Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);

        // El corte es por estado en la base, no por invalidar el token: al
        // reactivar la cuenta el mismo token vuelve a servir.
        await SetActiveAsync(clientId, isActive: true);
        var allowed = await client.GetAsync("/files");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task ClienteDadoDeBaja_SuApiKeyDejaDeAutenticar()
    {
        var (clientId, email, password) = await fixture.CreateClientAsync();
        var jwt = await fixture.LoginAsync(email, password);

        using (var panel = fixture.AuthenticatedClient(jwt))
        {
            var created = await panel.PostAsJsonAsync("/me/api-keys",
                new { name = "clave-antes-de-la-baja" });
            created.EnsureSuccessStatusCode();
            var key = await created.Content.ReadFromJsonAsync<CreateKeyResult>();

            using var apiClient = fixture.ApiKeyClient(key!.Value);

            var beforeDelete = await apiClient.GetAsync("/files");
            Assert.Equal(HttpStatusCode.OK, beforeDelete.StatusCode);

            await DeleteClientAsync(clientId);

            // La key sigue activa como fila, pero su dueño ya no existe.
            var afterDelete = await apiClient.GetAsync("/files");
            Assert.Equal(HttpStatusCode.Unauthorized, afterDelete.StatusCode);
        }
    }

    [Fact]
    public async Task ClienteDadoDeBaja_NoPuedeVolverAIniciarSesion()
    {
        var (clientId, email, password) = await fixture.CreateClientAsync();
        await DeleteClientAsync(clientId);

        using var client = fixture.Factory.CreateClient();
        var login = await client.PostAsJsonAsync("/auth/login", new { email, password });

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_NoSeVeAfectadoPorLaComprobacionDeCliente()
    {
        // El super-admin no tiene claim client_id: la comprobacion debe saltarselo
        // por completo, no tratarlo como un cliente inexistente.
        var adminToken = await fixture.LoginAsAdminAsync();
        using var admin = fixture.AuthenticatedClient(adminToken);

        var response = await admin.GetAsync("/admin/clients");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
