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
    private record CreateClientResponse(ClientPayload Client, string GeneratedPassword);
    public record ClientPayload(Guid Id, string Email, string Name);

    /// <summary>Token JWT del super-admin.</summary>
    public async Task<string> LoginAsAdminAsync()
    {
        return await LoginAsync(TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);
    }

    public async Task<string> LoginAsync(string email, string password)
    {
        using var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    /// <summary>Crea un cliente y devuelve su id, email y contraseña generada.</summary>
    public async Task<(Guid Id, string Email, string Password)> CreateClientAsync(long quotaBytes = 10 * 1024 * 1024)
    {
        var adminToken = await LoginAsAdminAsync();
        using var client = AuthenticatedClient(adminToken);

        var email = $"it-{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync("/admin/clients", new
        {
            email,
            name = "Cliente Integracion",
            quotaBytes
        });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<CreateClientResponse>();
        return (body!.Client.Id, email, body.GeneratedPassword);
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
