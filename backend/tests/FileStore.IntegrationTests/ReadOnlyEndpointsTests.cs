using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace FileStore.IntegrationTests;

/// <summary>
/// Cubre los tres endpoints de solo lectura que quedaban sin tocar. No tienen
/// logica propia mas alla de proyectar datos, asi que lo que se comprueba es que
/// respondan, que respeten el aislamiento y que no expongan de mas.
///
/// Con esto los 43 endpoints de la API tienen al menos un test que los ejerce.
/// </summary>
[Collection("Integration")]
public class ReadOnlyEndpointsTests(IntegrationTestFixture fixture)
{
    private record ClientDto(
        Guid Id, string Email, string Name, long QuotaBytes, long UsedBytes, bool IsActive);
    private record DailyActivityDto(DateTime Date, int Uploads, int Downloads, long BytesUploaded);
    private record ClientStatsDto(List<DailyActivityDto> Daily);
    private record AdminWhoAmI(string UserId, string Email, string Role);

    // --- GET /admin/clients/{id} ---------------------------------------------

    [Fact]
    public async Task DetalleDeCliente_DevuelveSusDatos()
    {
        var (clientId, email, _) = await fixture.CreateClientAsync(quotaBytes: 4096);

        var adminToken = await fixture.LoginAsAdminAsync();
        using var admin = fixture.AuthenticatedClient(adminToken);

        var client = await admin.GetFromJsonAsync<ClientDto>($"/admin/clients/{clientId}");

        Assert.Equal(clientId, client!.Id);
        Assert.Equal(email, client.Email);
        Assert.Equal(4096, client.QuotaBytes);
    }

    [Fact]
    public async Task DetalleDeCliente_NoExpuestoAlPropioCliente()
    {
        var (clientId, email, password) = await fixture.CreateClientAsync();
        var jwt = await fixture.LoginAsync(email, password);

        using var client = fixture.AuthenticatedClient(jwt);

        // Todo /admin es del super-admin. Un cliente consulta lo suyo por /me.
        var response = await client.GetAsync($"/admin/clients/{clientId}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DetalleDeCliente_Inexistente_Devuelve404()
    {
        var adminToken = await fixture.LoginAsAdminAsync();
        using var admin = fixture.AuthenticatedClient(adminToken);

        var response = await admin.GetAsync($"/admin/clients/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- GET /admin/whoami ----------------------------------------------------

    [Fact]
    public async Task AdminWhoAmI_DevuelveLaIdentidadDelToken()
    {
        var adminToken = await fixture.LoginAsAdminAsync();
        using var admin = fixture.AuthenticatedClient(adminToken);

        var identidad = await admin.GetFromJsonAsync<AdminWhoAmI>("/admin/whoami");

        Assert.Equal(TestWebApplicationFactory.AdminEmail, identidad!.Email);
        Assert.Equal("SuperAdmin", identidad.Role);
        Assert.False(string.IsNullOrWhiteSpace(identidad.UserId));
    }

    [Fact]
    public async Task AdminWhoAmI_ConTokenDeCliente_Devuelve403()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var jwt = await fixture.LoginAsync(email, password);

        using var client = fixture.AuthenticatedClient(jwt);

        var response = await client.GetAsync("/admin/whoami");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- GET /me/stats --------------------------------------------------------

    [Fact]
    public async Task EstadisticasDelCliente_ReflejanLaActividad()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var jwt = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(jwt);

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("contenido de prueba")), "file", "medido.txt");
        (await client.PostAsync("/files", form)).EnsureSuccessStatusCode();

        var stats = await client.GetFromJsonAsync<ClientStatsDto>("/me/stats");

        Assert.NotNull(stats);
        Assert.NotEmpty(stats!.Daily);
        Assert.True(stats.Daily.Sum(d => d.Uploads) >= 1, "La subida deberia aparecer en las estadisticas.");
    }

    [Fact]
    public async Task EstadisticasDelCliente_NoMezclanActividadDeOtro()
    {
        var (_, emailA, passwordA) = await fixture.CreateClientAsync();
        var tokenA = await fixture.LoginAsync(emailA, passwordA);
        using var clientA = fixture.AuthenticatedClient(tokenA);

        // A sube tres archivos.
        for (var i = 0; i < 3; i++)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("x")), "file", $"de-a-{i}.txt");
            (await clientA.PostAsync("/files", form)).EnsureSuccessStatusCode();
        }

        var (_, emailB, passwordB) = await fixture.CreateClientAsync();
        var tokenB = await fixture.LoginAsync(emailB, passwordB);
        using var clientB = fixture.AuthenticatedClient(tokenB);

        var deB = await clientB.GetFromJsonAsync<ClientStatsDto>("/me/stats");

        // El aislamiento tambien aplica a los agregados: si las cifras de B
        // incluyeran la actividad de A, revelarian su volumen de uso.
        Assert.Equal(0, deB!.Daily.Sum(d => d.Uploads));
    }

    [Fact]
    public async Task EstadisticasDelCliente_ConRangoInvalido_Devuelve400()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var jwt = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(jwt);

        // El rango esta acotado a 1..365 para que nadie pida un agregado de diez
        // años y tumbe la consulta.
        var response = await client.GetAsync("/me/stats?days=5000");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
