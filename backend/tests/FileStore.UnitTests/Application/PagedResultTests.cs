using FileStore.Application.Common.Models;

namespace FileStore.UnitTests.Application;

public class PagedResultTests
{
    [Theory]
    [InlineData(0, 20, 0)]    // sin items: cero paginas
    [InlineData(20, 20, 1)]   // justo una pagina
    [InlineData(21, 20, 2)]   // un item mas: dos paginas
    [InlineData(100, 25, 4)]
    [InlineData(1, 50, 1)]
    public void TotalPages_SeCalculaBien(int total, int pageSize, int expectedPages)
    {
        var result = new PagedResult<string>([], 1, pageSize, total);
        Assert.Equal(expectedPages, result.TotalPages);
    }

    [Fact]
    public void HasNextPage_EnLaUltimaPagina_EsFalse()
    {
        // Pagina 2 de 2: no hay siguiente.
        var result = new PagedResult<string>([], Page: 2, PageSize: 20, TotalCount: 40);
        Assert.False(result.HasNextPage);
    }

    [Fact]
    public void HasNextPage_ConPaginasPorDelante_EsTrue()
    {
        var result = new PagedResult<string>([], Page: 1, PageSize: 20, TotalCount: 40);
        Assert.True(result.HasNextPage);
    }

    [Fact]
    public void PageSizeCero_NoDivideEntreCero()
    {
        // Guarda contra division por cero: TotalPages debe dar 0, no lanzar.
        var result = new PagedResult<string>([], 1, 0, 10);
        Assert.Equal(0, result.TotalPages);
    }
}
