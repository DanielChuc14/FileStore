using System.Net;
using System.Net.Http.Json;

namespace FileStore.IntegrationTests;

/// <summary>
/// Cubre el punto delicado de las carpetas: el Path es un denormalizado cacheado,
/// asi que renombrar o mover una carpeta debe recalcular el Path de TODA su
/// descendencia. Si no, los hijos quedan apuntando a una ruta que ya no existe,
/// sin que nada lance un error. Tambien cubre que no se pueda mover una carpeta
/// dentro de su propio subarbol.
/// </summary>
[Collection("Integration")]
public class FolderTests(IntegrationTestFixture fixture)
{
    private record FolderDto(Guid Id, Guid? ParentFolderId, string Name, string Path, DateTime CreatedAt);

    private static async Task<FolderDto> CreateFolderAsync(HttpClient client, string name, Guid? parentId)
    {
        var response = await client.PostAsJsonAsync("/folders", new { name, parentId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FolderDto>())!;
    }

    private static async Task<FolderDto> GetFolderAsync(HttpClient client, Guid id)
    {
        var all = await client.GetFromJsonAsync<List<FolderDto>>("/folders?all=true");
        return all!.Single(f => f.Id == id);
    }

    /// <summary>Crea /docs -> /docs/2026 -> /docs/2026/facturas y los devuelve.</summary>
    private static async Task<(FolderDto Docs, FolderDto Anio, FolderDto Facturas)> CreateTreeAsync(HttpClient client)
    {
        var docs = await CreateFolderAsync(client, "docs", null);
        var anio = await CreateFolderAsync(client, "2026", docs.Id);
        var facturas = await CreateFolderAsync(client, "facturas", anio.Id);

        // Precondicion: los paths cacheados se armaron bien al crear.
        Assert.Equal("/docs", docs.Path);
        Assert.Equal("/docs/2026", anio.Path);
        Assert.Equal("/docs/2026/facturas", facturas.Path);

        return (docs, anio, facturas);
    }

    [Fact]
    public async Task Rename_RecalculaElPathDeTodaLaDescendencia()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        var (docs, anio, facturas) = await CreateTreeAsync(client);

        // Renombrar la raiz del arbol.
        var rename = await client.PatchAsJsonAsync($"/folders/{docs.Id}", new { name = "documentos" });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);

        // Toda la descendencia refleja el nombre nuevo en su Path.
        Assert.Equal("/documentos", (await GetFolderAsync(client, docs.Id)).Path);
        Assert.Equal("/documentos/2026", (await GetFolderAsync(client, anio.Id)).Path);
        Assert.Equal("/documentos/2026/facturas", (await GetFolderAsync(client, facturas.Id)).Path);
    }

    [Fact]
    public async Task Rename_NoAfectaHermanasConPrefijoComun()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        // Dos raices cuyo nombre comparte prefijo: /doc y /docs.
        var doc = await CreateFolderAsync(client, "doc", null);
        var docs = await CreateFolderAsync(client, "docs", null);

        // Renombrar /doc no debe tocar /docs (el recalculo filtra por "oldPath/").
        await client.PatchAsJsonAsync($"/folders/{doc.Id}", new { name = "carpeta" });

        Assert.Equal("/carpeta", (await GetFolderAsync(client, doc.Id)).Path);
        Assert.Equal("/docs", (await GetFolderAsync(client, docs.Id)).Path);
    }

    [Fact]
    public async Task Move_RecalculaElPathDelSubarbolMovido()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        var (_, anio, facturas) = await CreateTreeAsync(client);
        var destino = await CreateFolderAsync(client, "archivo", null);

        // Mover /docs/2026 (con su hijo facturas) bajo /archivo.
        var move = await client.PatchAsJsonAsync($"/folders/{anio.Id}", new { parentId = destino.Id });
        Assert.Equal(HttpStatusCode.OK, move.StatusCode);

        Assert.Equal("/archivo/2026", (await GetFolderAsync(client, anio.Id)).Path);
        Assert.Equal("/archivo/2026/facturas", (await GetFolderAsync(client, facturas.Id)).Path);
    }

    [Fact]
    public async Task Move_DentroDeSuPropioSubarbol_409()
    {
        var (_, email, password) = await fixture.CreateClientAsync();
        var token = await fixture.LoginAsync(email, password);
        using var client = fixture.AuthenticatedClient(token);

        var (docs, _, facturas) = await CreateTreeAsync(client);

        // Mover /docs dentro de /docs/2026/facturas desconectaria el subarbol:
        // debe rechazarse con 409.
        var move = await client.PatchAsJsonAsync($"/folders/{docs.Id}", new { parentId = facturas.Id });
        Assert.Equal(HttpStatusCode.Conflict, move.StatusCode);
    }
}
