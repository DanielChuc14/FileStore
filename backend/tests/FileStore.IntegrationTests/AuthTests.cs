using System.Net;
using System.Net.Http.Json;

namespace FileStore.IntegrationTests;

[Collection("Integration")]
public class AuthTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task Login_CredencialesValidas_DevuelveToken()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        using var client = fixture.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login", new { email, password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_ContrasenaIncorrecta_401()
    {
        var (_, email, _) = await fixture.CreateClientAsync();
        using var client = fixture.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login", new { email, password = "incorrecta" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_EmailInexistente_401()
    {
        using var client = fixture.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login",
            new { email = "no-existe@example.com", password = "loquesea123" });

        // Mismo codigo que contraseña incorrecta: no se revela si el email existe.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_DevuelveCookieHttpOnly()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        using var client = fixture.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login", new { email, password });

        // El refresh token viaja en cookie con los flags de seguridad, no en el body.
        var setCookie = response.Headers.GetValues("Set-Cookie").First();
        Assert.Contains("fs_refresh", setCookie);
        Assert.Contains("httponly", setCookie.ToLowerInvariant());
        Assert.Contains("samesite=strict", setCookie.ToLowerInvariant());

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("refreshToken", body);
    }

    [Fact]
    public async Task EndpointDeCliente_ConTokenDeAdmin_403()
    {
        var adminToken = await fixture.LoginAsAdminAsync();
        using var client = fixture.AuthenticatedClient(adminToken);

        // El super-admin no tiene contenido propio: /me/usage le da 403.
        var response = await client.GetAsync("/me/usage");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EndpointDeAdmin_ConTokenDeCliente_403()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        var response = await client.GetAsync("/admin/clients");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EndpointProtegido_SinToken_401()
    {
        using var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync("/me/usage");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
