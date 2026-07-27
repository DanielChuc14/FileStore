using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FileStore.IntegrationTests;

/// <summary>
/// Fixture compartido: la API se levanta y migra una sola vez para toda la
/// coleccion de tests, en vez de por cada test. Ademas ofrece helpers para las
/// tareas repetidas (login, crear cliente, obtener un HttpClient autenticado).
/// </summary>
public class IntegrationTestFixture : IAsyncLifetime
{
    public TestWebApplicationFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Factory = new TestWebApplicationFactory();
        await Factory.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        Factory.Dispose();
        return Task.CompletedTask;
    }

    private record LoginResponse(string AccessToken, string Email, string Role);
    public record ClientPayload(Guid Id, string Email, string Name);

    /// <summary>
    /// Contraseña que el fixture asigna a los clientes que crea. El alta ya no
    /// devuelve la generada (se la lleva el correo), asi que los tests fijan una
    /// conocida para poder iniciar sesion.
    /// </summary>
    public const string ClientPassword = "ClienteDePrueba2026";

    /// <summary>Token JWT del super-admin.</summary>
    public async Task<string> LoginAsAdminAsync()
    {
        return await LoginAsync(TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);
    }

    public async Task<string> LoginAsync(string email, string password)
    {
        using var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/v1/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    /// <summary>
    /// Crea un cliente y le fija una contraseña conocida para poder operar con
    /// el. Devuelve su id, email y esa contraseña.
    /// </summary>
    public async Task<(Guid Id, string Email, string Password)> CreateClientAsync(long quotaBytes = 10 * 1024 * 1024)
    {
        var adminToken = await LoginAsAdminAsync();
        using var client = AuthenticatedClient(adminToken);

        var email = $"it-{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync("/v1/admin/clients", new
        {
            email,
            name = "Cliente Integracion",
            quotaBytes
        });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ClientPayload>();

        // La generada solo existe dentro del correo encolado; se pisa por una
        // conocida para que el test pueda autenticarse.
        await Factory.SetClientPasswordAsync(body!.Id, ClientPassword);

        return (body.Id, email, ClientPassword);
    }

    /// <summary>Cliente HTTP con el bearer token ya puesto.</summary>
    public HttpClient AuthenticatedClient(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Cliente HTTP autenticado con una API Key en el header.</summary>
    public HttpClient ApiKeyClient(string apiKey)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        return client;
    }
}

[CollectionDefinition("Integration")]
public class IntegrationCollection : ICollectionFixture<IntegrationTestFixture>;
