using System.Net;
using System.Net.Http.Json;

namespace FileStore.IntegrationTests;

/// <summary>
/// Cubre los endpoints de cuenta que faltaban: cambio de contraseña, cierre de
/// sesion, perfil y los tipos de archivo permitidos.
///
/// El cambio de contraseña tiene una particularidad que ya causo un bug: devuelve
/// 401 cuando la contraseña ACTUAL es incorrecta, y ese 401 no significa sesion
/// vencida. El interceptor del panel tuvo que aprender a distinguirlos.
/// </summary>
[Collection("Integration")]
public class AccountTests(IntegrationTestFixture fixture)
{
    private const string NewPassword = "OtraContrasena2026";

    private record ProfileDto(Guid Id, string Email, string Name);
    private record AllowedTypeDto(Guid Id, string Extension, string MimeType, bool IsEnabled);
    private record WhoAmIDto(Guid ClientId, string ClientName, Guid ApiKeyId);
    private record CreateKeyResult(string Value);

    // --- Cambio de contraseña -------------------------------------------------

    [Fact]
    public async Task CambiarContrasena_ConLaActualCorrecta_Funciona()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var jwt = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(jwt);

        var response = await client.PostAsJsonAsync("/v1/me/change-password", new
        {
            currentPassword = password,
            newPassword = NewPassword
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // La nueva sirve y la vieja no.
        await fixture.LoginAsync(email, NewPassword);

        using var anonymous = fixture.Factory.CreateClient();
        var conLaVieja = await anonymous.PostAsJsonAsync("/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.Unauthorized, conLaVieja.StatusCode);
    }

    [Fact]
    public async Task CambiarContrasena_ConLaActualIncorrecta_Devuelve401()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var jwt = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(jwt);

        var response = await client.PostAsJsonAsync("/v1/me/change-password", new
        {
            currentPassword = "no-es-la-mia",
            newPassword = NewPassword
        });

        // Este 401 es "tu contraseña actual esta mal", NO "tu sesion vencio". El
        // interceptor del panel excluye este endpoint del refresh automatico por
        // esto mismo: si no, escribir mal la contraseña deslogueaba al usuario.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // Y la sesion sigue viva, que es la otra mitad del asunto.
        var sigueDentro = await client.GetAsync("/v1/files");
        Assert.Equal(HttpStatusCode.OK, sigueDentro.StatusCode);

        // La contraseña original no cambio.
        await fixture.LoginAsync(email, password);
    }

    [Fact]
    public async Task CambiarContrasena_ConUnaDebil_Devuelve400()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var jwt = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(jwt);

        var response = await client.PostAsJsonAsync("/v1/me/change-password", new
        {
            currentPassword = password,
            newPassword = "corta"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CambiarContrasena_PorLaMisma_Devuelve400()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var jwt = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(jwt);

        var response = await client.PostAsJsonAsync("/v1/me/change-password", new
        {
            currentPassword = password,
            newPassword = password
        });

        // Cambiarla por la misma no cambia nada, y da la falsa sensacion de haber
        // reaccionado ante un posible compromiso.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- Logout ---------------------------------------------------------------

    [Fact]
    public async Task Logout_RevocaElRefreshTokenYBorraLaCookie()
    {
        var (_, email, password) = await fixture.CreateClientAsync();

        using var client = fixture.Factory.CreateClient();
        var login = await client.PostAsJsonAsync("/v1/auth/login", new { email, password });
        var cookie = login.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("fs_refresh="))
            .Split(';')[0];

        using var sesion = fixture.Factory.CreateClient();
        sesion.DefaultRequestHeaders.Add("Cookie", cookie);

        var logout = await sesion.PostAsync("/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // La cookie se borra pidiendo al navegador que la expire.
        var setCookie = logout.Headers.TryGetValues("Set-Cookie", out var values)
            ? string.Join(";", values)
            : string.Empty;
        Assert.Contains("fs_refresh=", setCookie);

        // Y el token queda revocado del lado del servidor, que es lo que de
        // verdad cierra la sesion: borrar la cookie solo afecta a ese navegador.
        using var replay = fixture.Factory.CreateClient();
        replay.DefaultRequestHeaders.Add("Cookie", cookie);
        var refresh = await replay.PostAsync("/v1/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Logout_SinCookie_NoFalla()
    {
        using var anonymous = fixture.Factory.CreateClient();

        // Es idempotente a proposito: cerrar sesion sin tenerla no es un error, y
        // el panel lo llama tambien cuando ya expiro todo.
        var response = await anonymous.PostAsync("/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // --- Perfil y whoami ------------------------------------------------------

    [Fact]
    public async Task Perfil_DevuelveLosDatosDelClienteAutenticado()
    {
        var (clientId, email, password) = await fixture.CreateClientAsync();
        var jwt = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(jwt);

        var profile = await client.GetFromJsonAsync<ProfileDto>("/v1/me");

        Assert.Equal(clientId, profile!.Id);
        Assert.Equal(email, profile.Email);
    }

    [Fact]
    public async Task WhoAmI_ResuelveLaApiKeyContraSuCliente()
    {
        var (clientId, email, password) = await fixture.CreateClientAsync();
        var jwt = await fixture.LoginAsync(email, password);

        using var panel = fixture.AuthenticatedClient(jwt);
        var created = await panel.PostAsJsonAsync("/v1/me/api-keys", new { name = "whoami" });
        var key = await created.Content.ReadFromJsonAsync<CreateKeyResult>();

        using var apiClient = fixture.ApiKeyClient(key!.Value);
        var whoami = await apiClient.GetFromJsonAsync<WhoAmIDto>("/v1/whoami");

        // Existe para que una integracion pueda comprobar su credencial sin
        // tener que subir un archivo de prueba.
        Assert.Equal(clientId, whoami!.ClientId);
        Assert.NotEqual(Guid.Empty, whoami.ApiKeyId);
    }

    [Fact]
    public async Task WhoAmI_ConJwtDelPanel_NoAutentica()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var jwt = await fixture.LoginAsync(email, password);

        using var panel = fixture.AuthenticatedClient(jwt);

        // Es el unico endpoint con politica de API Key pura: un JWT no lo abre,
        // que es lo que mantiene separados los dos canales.
        var response = await panel.GetAsync("/v1/whoami");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Tipos de archivo permitidos ------------------------------------------

    [Fact]
    public async Task DesactivarUnTipo_HaceQueSuSubidaFalle()
    {
        var adminToken = await fixture.LoginAsAdminAsync();
        using var admin = fixture.AuthenticatedClient(adminToken);

        var tipos = await admin.GetFromJsonAsync<List<AllowedTypeDto>>("/v1/admin/allowed-types");
        var csv = tipos!.Single(t => t.Extension == "csv");

        var (_, email, password) = await fixture.CreateClientAsync();
        var jwt = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(jwt);

        try
        {
            var patch = await admin.PatchAsJsonAsync($"/v1/admin/allowed-types/{csv.Id}", new { isEnabled = false });
            Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent("a,b,c"u8.ToArray()), "file", "datos.csv");

            // La whitelist es configuracion viva: desactivar un tipo tiene que
            // surtir efecto en la siguiente subida, sin reiniciar nada.
            var upload = await client.PostAsync("/v1/files", form);
            Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
        }
        finally
        {
            // Se restaura: la base es compartida por toda la coleccion y dejar el
            // csv desactivado rompería otros tests.
            await admin.PatchAsJsonAsync($"/v1/admin/allowed-types/{csv.Id}", new { isEnabled = true });
        }

        // Y al reactivarlo vuelve a entrar.
        using var form2 = new MultipartFormDataContent();
        form2.Add(new ByteArrayContent("a,b,c"u8.ToArray()), "file", "datos.csv");
        var ahora = await client.PostAsync("/v1/files", form2);
        Assert.Equal(HttpStatusCode.Created, ahora.StatusCode);
    }

    [Fact]
    public async Task LosTiposPermitidos_SoloLosVeElSuperAdmin()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var jwt = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(jwt);

        var response = await client.GetAsync("/v1/admin/allowed-types");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
