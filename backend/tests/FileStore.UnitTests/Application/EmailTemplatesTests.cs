using FileStore.Application.Abstractions;
using FileStore.Application.Common.Emails;

namespace FileStore.UnitTests.Application;

/// <summary>
/// Las plantillas son funciones puras, asi que se prueban sin base ni red.
///
/// Lo que se fija aqui son propiedades que un rediseño podria romper sin que
/// nada mas se entere: que el nombre del cliente vaya escapado, que exista la
/// version en texto plano, y que no haya recursos remotos.
/// </summary>
public class EmailTemplatesTests
{
    private const string Panel = "https://filestore.example.com";

    /// <summary>Una de cada plantilla, para las comprobaciones transversales.</summary>
    public static TheoryData<string, EmailMessage> TodasLasPlantillas() => new()
    {
        { "Welcome", EmailTemplates.Welcome("a@b.com", "Ana", "Secreta123456", Panel) },
        { "PasswordReset", EmailTemplates.PasswordReset("a@b.com", "Ana", "Secreta123456", Panel) },
        { "PasswordResetLink", EmailTemplates.PasswordResetLink("a@b.com", "Ana", $"{Panel}/reset?token=abc", 60) },
        { "QuotaAlert", EmailTemplates.QuotaAlert("a@b.com", "Ana", 80, 800, 1000, Panel) },
        { "SuspiciousSessionActivity", EmailTemplates.SuspiciousSessionActivity("a@b.com", "Ana", Panel) },
        { "ApiKeyActivity", EmailTemplates.ApiKeyActivity("a@b.com", "Ana", "produccion", "fs_live_ABC", false, Panel) }
    };

    [Theory]
    [MemberData(nameof(TodasLasPlantillas))]
    public void TodaPlantilla_TieneAsuntoDestinatarioYAmbosCuerpos(string nombre, EmailMessage message)
    {
        Assert.False(string.IsNullOrWhiteSpace(message.Subject), $"{nombre}: sin asunto.");
        Assert.False(string.IsNullOrWhiteSpace(message.To), $"{nombre}: sin destinatario.");
        Assert.False(string.IsNullOrWhiteSpace(message.HtmlBody), $"{nombre}: sin cuerpo HTML.");

        // El texto plano no es un adorno: sin el, los filtros anti-spam penalizan
        // el mensaje y algunos clientes solo muestran esa parte.
        Assert.False(string.IsNullOrWhiteSpace(message.TextBody), $"{nombre}: sin texto plano.");
    }

    [Theory]
    [MemberData(nameof(TodasLasPlantillas))]
    public void TodaPlantilla_EsUnDocumentoCompleto(string nombre, EmailMessage message)
    {
        Assert.StartsWith("<!doctype html>", message.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<html lang=\"es\">", message.HtmlBody);
        Assert.Contains("</html>", message.HtmlBody);
    }

    [Theory]
    [MemberData(nameof(TodasLasPlantillas))]
    public void TodaPlantilla_NoCargaRecursosRemotos(string nombre, EmailMessage message)
    {
        // La mayoria de los clientes bloquea las imagenes remotas por defecto, asi
        // que un logo enlazado se veria como un hueco roto. Ademas, una peticion a
        // un servidor externo al abrir el correo es un pixel de rastreo de facto.
        Assert.DoesNotContain("<img", message.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("src=\"http", message.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@import", message.HtmlBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ElNombreDelCliente_VaEscapado()
    {
        var message = EmailTemplates.Welcome(
            "victima@example.com",
            "<script>alert(1)</script>",
            "Secreta123456",
            Panel);

        // El nombre lo elige quien da de alta al cliente, y el correo lo lee otra
        // persona: sin escapar, seria inyeccion en el buzon ajeno.
        Assert.DoesNotContain("<script>", message.HtmlBody);
        Assert.Contains("&lt;script&gt;", message.HtmlBody);
    }

    [Fact]
    public void LaContrasenaGenerada_VaEnAmbasVersiones()
    {
        const string password = "Xk9#mQ2pLvN4";
        var message = EmailTemplates.Welcome("a@b.com", "Ana", password, Panel);

        Assert.Contains(password, message.HtmlBody);

        // El marcador "Contrasena:" es el que usan los tests de integracion para
        // extraerla del correo encolado.
        Assert.Contains($"Contrasena: {password}", message.TextBody);
    }

    [Fact]
    public void ElEnlaceDeRecuperacion_VaEnAmbasVersiones()
    {
        var url = $"{Panel}/reset-password?token=un-token-largo";
        var message = EmailTemplates.PasswordResetLink("a@b.com", "Ana", url, 60);

        Assert.Contains(url, message.HtmlBody);
        Assert.Contains(url, message.TextBody);

        // Se repite en texto visible por si el boton no se renderiza.
        Assert.Contains("60 minutos", message.TextBody);
    }

    [Theory]
    [InlineData(80, false)]
    [InlineData(95, true)]
    public void ElAvisoDeCuota_LlevaElPorcentajeEnElAsunto(int percent, bool critical)
    {
        var message = EmailTemplates.QuotaAlert("a@b.com", "Ana", percent, 950, 1000, Panel);

        Assert.Contains($"{percent}%", message.Subject);

        // Al 95% el tono cambia: deja de ser informativo.
        var anunciaFallos = message.HtmlBody.Contains("empezaran a fallar");
        Assert.Equal(critical, anunciaFallos);
    }

    [Fact]
    public void ElAvisoDeCuota_MuestraLosTamanosLegibles()
    {
        var message = EmailTemplates.QuotaAlert(
            "a@b.com", "Ana", 80, 838_860_800, 1_073_741_824, Panel);

        // 800 MB de 1 GB, no dos numeros de nueve cifras.
        Assert.Contains("800 MB", message.HtmlBody);
        Assert.Contains("1 GB", message.HtmlBody);
    }

    [Theory]
    [InlineData(true, "roto")]
    [InlineData(false, "creado")]
    public void ElAvisoDeApiKey_DistingueCrearDeRotar(bool rotated, string esperado)
    {
        var message = EmailTemplates.ApiKeyActivity(
            "a@b.com", "Ana", "produccion", "fs_live_ABC", rotated, Panel);

        Assert.Contains(esperado, message.Subject);
        Assert.Contains("API Key", message.Subject);
    }

    [Fact]
    public void ElAvisoDeApiKey_NoRevelaLaClaveCompleta()
    {
        var message = EmailTemplates.ApiKeyActivity(
            "a@b.com", "Ana", "produccion", "fs_live_ABC", rotated: false, Panel);

        // Solo el prefijo, que es publico por diseño. El valor completo viaja una
        // unica vez, en la respuesta de creacion, y nunca por correo.
        Assert.Contains("fs_live_ABC", message.HtmlBody);
        Assert.DoesNotContain("fs_live_ABC.", message.HtmlBody);
    }
}
