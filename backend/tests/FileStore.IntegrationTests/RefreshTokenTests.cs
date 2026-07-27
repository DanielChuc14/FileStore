using System.Net;
using System.Net.Http.Json;

namespace FileStore.IntegrationTests;

/// <summary>
/// Cubre el ciclo de vida del refresh token: rotacion en cada uso y deteccion de
/// reuso. Es de lo mas sensible del sistema: si la rotacion o la revocacion de la
/// cadena fallan en silencio, un token robado da acceso indefinido.
///
/// La cookie fs_refresh es Secure, y el TestServer habla http, asi que el
/// CookieContainer no la reenviaria. Se maneja el token a mano: se extrae del
/// Set-Cookie y se reenvia como header Cookie, que ademas hace explicito que se
/// esta probando exactamente ese valor.
/// </summary>
[Collection("Integration")]
public class RefreshTokenTests(IntegrationTestFixture fixture)
{
    private static string ExtractRefreshToken(HttpResponseMessage response)
    {
        var cookie = response.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("fs_refresh=", StringComparison.Ordinal));
        return cookie["fs_refresh=".Length..].Split(';')[0];
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/refresh");
        request.Headers.Add("Cookie", $"fs_refresh={refreshToken}");
        return client.SendAsync(request);
    }

    [Fact]
    public async Task Refresh_RotaElTokenYElNuevoSigueSiendoValido()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        using var client = fixture.Factory.CreateClient();

        var login = await client.PostAsJsonAsync("/v1/auth/login", new { email, password });
        var t1 = ExtractRefreshToken(login);

        var r1 = await RefreshAsync(client, t1);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        var t2 = ExtractRefreshToken(r1);
        Assert.NotEqual(t1, t2); // rotacion: cada refresh emite un token distinto

        // El token rotado es plenamente valido: vuelve a refrescar y rota otra vez.
        var r2 = await RefreshAsync(client, t2);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.NotEqual(t2, ExtractRefreshToken(r2));
    }

    [Fact]
    public async Task Refresh_ReusoDeTokenRotado_RevocaLaCadenaEntera()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        using var client = fixture.Factory.CreateClient();

        var login = await client.PostAsJsonAsync("/v1/auth/login", new { email, password });
        var t1 = ExtractRefreshToken(login);

        var r1 = await RefreshAsync(client, t1);
        var t2 = ExtractRefreshToken(r1); // t2 pasa a ser el token vigente

        // Reusar t1, que ya fue rotado, solo se explica por robo: 401.
        var reuse = await RefreshAsync(client, t1);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        // Consecuencia clave de la deteccion de reuso: se revoca TODA la cadena,
        // asi que hasta t2 —que era el token legitimo y vigente— queda inservible.
        var afterReuse = await RefreshAsync(client, t2);
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode);
    }

    [Fact]
    public async Task Refresh_SinCookie_401()
    {
        using var client = fixture.Factory.CreateClient();

        var response = await client.PostAsync("/v1/auth/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
