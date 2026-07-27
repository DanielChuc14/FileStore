using System.Net;
using System.Net.Http.Json;

namespace FileStore.IntegrationTests;

/// <summary>
/// Cubre el rate limiting por API Key: superar el limite por minuto devuelve 429
/// con Retry-After. Es una defensa que o funciona o no; sin test, un fallo solo
/// se nota cuando ya abusaron de la API.
///
/// Se prueba el limite por API Key (no el de /auth por IP) porque el TestServer
/// no expone una IP remota: el limitador de /auth cae en su rama "sin IP" y no
/// aplica. El de API Key particiona por el id de la key, que si esta disponible.
/// </summary>
[Collection("Integration")]
public class RateLimitTests(IntegrationTestFixture fixture)
{
    private record CreateKeyResult(string Value);

    /// <summary>Crea una API Key con un limite por minuto explicito y devuelve su valor.</summary>
    private async Task<string> CreateApiKeyAsync(string jwt, int rateLimitPerMinute)
    {
        using var client = fixture.AuthenticatedClient(jwt);
        var response = await client.PostAsJsonAsync("/v1/me/api-keys",
            new { name = "rate-limit-test", rateLimitPerMinute });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreateKeyResult>();
        return result!.Value;
    }

    [Fact]
    public async Task ApiKey_SuperarElLimite_Devuelve429ConRetryAfter()
    {
        const int limit = 3;
        var (_, email, password) = await fixture.CreateClientAsync();
        var jwt = await fixture.LoginAsync(email, password);
        var apiKey = await CreateApiKeyAsync(jwt, limit);

        using var client = fixture.ApiKeyClient(apiKey);

        // Las primeras 'limit' peticiones pasan; se envian en serie para que el
        // conteo de la ventana sea determinista.
        for (var i = 0; i < limit; i++)
        {
            var ok = await client.GetAsync("/v1/files");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, ok.StatusCode);
        }

        // La siguiente supera el limite: 429 con Retry-After.
        var rejected = await client.GetAsync("/v1/files");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.Contains("Retry-After"),
            "La respuesta 429 debe incluir el header Retry-After.");
    }

    [Fact]
    public async Task ApiKey_LimitesPorKeySonIndependientes()
    {
        const int limit = 2;
        var (_, email, password) = await fixture.CreateClientAsync();
        var jwt = await fixture.LoginAsync(email, password);

        // Dos keys del mismo cliente: agotar una no debe afectar a la otra.
        var keyA = await CreateApiKeyAsync(jwt, limit);
        var keyB = await CreateApiKeyAsync(jwt, limit);

        using var clientA = fixture.ApiKeyClient(keyA);
        for (var i = 0; i < limit; i++)
        {
            await clientA.GetAsync("/v1/files");
        }
        var aRejected = await clientA.GetAsync("/v1/files");
        Assert.Equal(HttpStatusCode.TooManyRequests, aRejected.StatusCode);

        // keyB sigue con su cuota intacta.
        using var clientB = fixture.ApiKeyClient(keyB);
        var bResponse = await clientB.GetAsync("/v1/files");
        Assert.NotEqual(HttpStatusCode.TooManyRequests, bResponse.StatusCode);
    }
}
