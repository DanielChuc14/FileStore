using System.Net;
using System.Net.Http.Json;

namespace FileStore.IntegrationTests;

/// <summary>
/// Cubre la rotacion de API Keys. La invariante es la que hace util a la
/// rotacion frente a revocar y crear por separado: la key vieja deja de
/// autenticar y la nueva funciona, en una sola operacion, conservando nombre y
/// rate limit. Si la vieja siguiera viva, rotar tras una filtracion no serviria
/// de nada.
/// </summary>
[Collection("Integration")]
public class ApiKeyRotationTests(IntegrationTestFixture fixture)
{
    private record ApiKeyDto(
        Guid Id, string Name, string Prefix, int? RateLimitPerMinute,
        bool IsActive, DateTime? LastUsedAt, DateTime CreatedAt, DateTime? RevokedAt);
    private record CreateKeyResult(ApiKeyDto ApiKey, string Value);

    private static async Task<CreateKeyResult> CreateKeyAsync(
        HttpClient client, string name, int? rateLimitPerMinute = null)
    {
        var response = await client.PostAsJsonAsync("/v1/me/api-keys", new { name, rateLimitPerMinute });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreateKeyResult>())!;
    }

    [Fact]
    public async Task Rotate_LaKeyViejaDejaDeAutenticarYLaNuevaFunciona()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var jwt = await fixture.LoginAsync(email, password);
        using var panel = fixture.AuthenticatedClient(jwt);

        var original = await CreateKeyAsync(panel, "integracion");

        using (var conLaVieja = fixture.ApiKeyClient(original.Value))
        {
            var antes = await conLaVieja.GetAsync("/v1/files");
            Assert.Equal(HttpStatusCode.OK, antes.StatusCode);
        }

        var rotation = await panel.PostAsync($"/v1/me/api-keys/{original.ApiKey.Id}/rotate", null);
        Assert.Equal(HttpStatusCode.OK, rotation.StatusCode);
        var rotated = (await rotation.Content.ReadFromJsonAsync<CreateKeyResult>())!;

        // El valor nuevo es realmente nuevo, no el mismo devuelto otra vez.
        Assert.NotEqual(original.Value, rotated.Value);
        Assert.NotEqual(original.ApiKey.Prefix, rotated.ApiKey.Prefix);

        using (var conLaVieja = fixture.ApiKeyClient(original.Value))
        {
            var despues = await conLaVieja.GetAsync("/v1/files");
            Assert.Equal(HttpStatusCode.Unauthorized, despues.StatusCode);
        }

        using (var conLaNueva = fixture.ApiKeyClient(rotated.Value))
        {
            var response = await conLaNueva.GetAsync("/v1/files");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Rotate_ConservaElNombreYElRateLimit()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var jwt = await fixture.LoginAsync(email, password);
        using var panel = fixture.AuthenticatedClient(jwt);

        var original = await CreateKeyAsync(panel, "produccion", rateLimitPerMinute: 250);

        var rotation = await panel.PostAsync($"/v1/me/api-keys/{original.ApiKey.Id}/rotate", null);
        var rotated = (await rotation.Content.ReadFromJsonAsync<CreateKeyResult>())!;

        // Rotar no debe obligar a reconfigurar la key en el consumidor.
        Assert.Equal("produccion", rotated.ApiKey.Name);
        Assert.Equal(250, rotated.ApiKey.RateLimitPerMinute);
        Assert.True(rotated.ApiKey.IsActive);
        Assert.Null(rotated.ApiKey.RevokedAt);
    }

    [Fact]
    public async Task Rotate_DejaLaViejaRevocadaYVisibleEnElListado()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var jwt = await fixture.LoginAsync(email, password);
        using var panel = fixture.AuthenticatedClient(jwt);

        var original = await CreateKeyAsync(panel, "a-rotar");
        var rotation = await panel.PostAsync($"/v1/me/api-keys/{original.ApiKey.Id}/rotate", null);
        var rotated = (await rotation.Content.ReadFromJsonAsync<CreateKeyResult>())!;

        var keys = (await panel.GetFromJsonAsync<List<ApiKeyDto>>("/v1/me/api-keys"))!;

        // La vieja no se borra: queda como rastro auditable de que existio.
        var vieja = keys.Single(k => k.Id == original.ApiKey.Id);
        Assert.False(vieja.IsActive);
        Assert.NotNull(vieja.RevokedAt);

        var nueva = keys.Single(k => k.Id == rotated.ApiKey.Id);
        Assert.True(nueva.IsActive);
    }

    [Fact]
    public async Task Rotate_KeyDeOtroCliente_Devuelve404()
    {
        var (_, emailA, passwordA) = await fixture.CreateClientAsync();
        var jwtA = await fixture.LoginAsync(emailA, passwordA);
        using var panelA = fixture.AuthenticatedClient(jwtA);
        var keyDeA = await CreateKeyAsync(panelA, "de-a");

        var (_, emailB, passwordB) = await fixture.CreateClientAsync();
        var jwtB = await fixture.LoginAsync(emailB, passwordB);
        using var panelB = fixture.AuthenticatedClient(jwtB);

        var response = await panelB.PostAsync($"/v1/me/api-keys/{keyDeA.ApiKey.Id}/rotate", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Y la key de A sigue funcionando: el intento fallido no la toco.
        using var conLaDeA = fixture.ApiKeyClient(keyDeA.Value);
        var sigueViva = await conLaDeA.GetAsync("/v1/files");
        Assert.Equal(HttpStatusCode.OK, sigueViva.StatusCode);
    }
}
