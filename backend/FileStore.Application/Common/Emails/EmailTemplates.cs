using System.Net;
using FileStore.Application.Abstractions;
using static FileStore.Application.Common.Emails.EmailLayout;

namespace FileStore.Application.Common.Emails;

/// <summary>
/// Plantillas de los correos transaccionales.
///
/// Son funciones puras que devuelven el mensaje ya armado: sin motor de
/// plantillas ni dependencias, porque son pocas y cortas. Si algun dia crecen o
/// hay que traducirlas, el punto de cambio es este y nada mas. La maquetacion
/// compartida vive en <see cref="EmailLayout"/>.
///
/// Todo dato que venga del usuario (el nombre del cliente, sobre todo) pasa por
/// HtmlEncode: un cliente llamado &lt;script&gt; no puede inyectar nada en el
/// correo que recibe otra persona.
///
/// Cada plantilla devuelve tambien una version en texto plano. No es un adorno:
/// sin ella los filtros anti-spam penalizan el mensaje, y algunos clientes solo
/// muestran esa parte.
/// </summary>
public static class EmailTemplates
{
    public static EmailMessage Welcome(string to, string name, string password, string panelUrl)
    {
        const string subject = "Tu cuenta de FileStore esta lista";

        var html = Wrap(
            subject,
            "Estas son tus credenciales de acceso.",
            "Te damos la bienvenida",
            $"""
             {Paragraph($"Hola {Encode(name)},")}
             {Paragraph("Se creo tu cuenta en FileStore. Con estas credenciales puedes entrar al panel:")}
             {CredentialBox("Usuario", Encode(to))}
             {CredentialBox("Contrase&ntilde;a", Encode(password))}
             {Button(panelUrl, "Entrar al panel")}
             {Notice("Cambia la contrase&ntilde;a en cuanto entres. Esta es la unica vez que se te envia.")}
             {Muted("Si no esperabas este correo, ignoralo y avisa a quien administra el servicio.")}
             """);

        var text = $"""
            Hola {name},

            Se creo tu cuenta en FileStore. Estas son tus credenciales:

            Usuario: {to}
            Contrasena: {password}

            Entra en {panelUrl} y cambiala en cuanto puedas. Esta es la unica vez
            que se te envia.

            Si no esperabas este correo, ignoralo y avisa a quien administra el servicio.
            """;

        return new EmailMessage(to, subject, html, text);
    }

    public static EmailMessage PasswordReset(string to, string name, string password, string panelUrl)
    {
        const string subject = "Se restablecio tu contrasena de FileStore";

        var html = Wrap(
            subject,
            "Tu nueva contrase&ntilde;a y el aviso de que cerramos tus sesiones.",
            "Se restablecio tu contrase&ntilde;a",
            $"""
             {Paragraph($"Hola {Encode(name)},")}
             {Paragraph("Quien administra el servicio restablecio tu contrase&ntilde;a. La nueva es:")}
             {CredentialBox("Contrase&ntilde;a", Encode(password))}
             {Button(panelUrl, "Entrar al panel")}
             {Notice("Todas tus sesiones abiertas se cerraron. Cambia la contrase&ntilde;a en cuanto entres.")}
             {Muted("<strong>Si no pediste este cambio</strong>, avisa cuanto antes: puede indicar que alguien accedio a tu cuenta.")}
             """);

        // El formato "Contrasena: X" es el mismo que en el correo de alta, para
        // que los dos sitios donde viaja una credencial se lean igual.
        var text = $"""
            Hola {name},

            Quien administra el servicio restablecio tu contrasena.

            Contrasena: {password}

            Entra en {panelUrl} y cambiala en cuanto puedas. Todas tus sesiones
            abiertas se cerraron.

            Si no pediste este cambio, avisa cuanto antes.
            """;

        return new EmailMessage(to, subject, html, text);
    }

    public static EmailMessage PasswordResetLink(
        string to,
        string name,
        string resetUrl,
        int minutesValid)
    {
        const string subject = "Recupera tu contrasena de FileStore";

        var html = Wrap(
            subject,
            $"Enlace de un solo uso, valido durante {minutesValid} minutos.",
            "Recupera tu contrase&ntilde;a",
            $"""
             {Paragraph($"Hola {Encode(name)},")}
             {Paragraph($"Pediste recuperar el acceso a tu cuenta. Este enlace sirve <strong>una sola vez</strong> y vence en {minutesValid} minutos:")}
             {Button(resetUrl, "Elegir una contrase&ntilde;a nueva")}
             {Muted($"Si el boton no funciona, copia esta direccion en tu navegador:<br><span style=\"word-break:break-all;color:#64748b;\">{resetUrl}</span>")}
             {Notice("<strong>Si no pediste esto</strong>, ignora el correo: tu contrase&ntilde;a sigue igual y el enlace vence solo.")}
             """);

        var text = $"""
            Hola {name},

            Pediste recuperar el acceso a tu cuenta de FileStore. Este enlace sirve
            una sola vez y vence en {minutesValid} minutos:

            {resetUrl}

            Si no pediste esto, ignora el correo: tu contrasena sigue igual y el
            enlace vence solo.
            """;

        return new EmailMessage(to, subject, html, text);
    }

    public static EmailMessage QuotaAlert(
        string to,
        string name,
        int percent,
        long usedBytes,
        long quotaBytes,
        string panelUrl)
    {
        var subject = $"FileStore: tu espacio esta al {percent}%";

        var used = FormatBytes(usedBytes);
        var quota = FormatBytes(quotaBytes);

        // Al 95% el mensaje cambia de tono: ya no es informativo, es inminente.
        var critical = percent >= 95;

        var heading = critical
            ? "Tu espacio esta por agotarse"
            : $"Tu espacio esta al {percent}%";

        var advice = critical
            ? "Cuando se llene, las subidas empezaran a fallar."
            : "Todavia tienes margen, pero conviene que lo revises.";

        var html = Wrap(
            subject,
            $"Estas usando {used} de {quota}.",
            heading,
            $"""
             {Paragraph($"Hola {Encode(name)},")}
             {Paragraph($"Estas usando <strong>{used}</strong> de <strong>{quota}</strong> ({percent}%). {advice}")}
             {ProgressBar(percent, critical)}
             {Notice("La papelera y las versiones antiguas <strong>tambien ocupan cuota</strong>. Vaciar la papelera desde el panel suele liberar bastante.")}
             {Button(panelUrl, "Revisar mi espacio")}
             """);

        var text = $"""
            Hola {name},

            Estas usando {used} de {quota} ({percent}%). {advice}

            La papelera y las versiones antiguas tambien ocupan cuota: vaciar la
            papelera desde el panel suele liberar bastante.

            {panelUrl}
            """;

        return new EmailMessage(to, subject, html, text);
    }

    public static EmailMessage SuspiciousSessionActivity(string to, string name, string panelUrl)
    {
        const string subject = "FileStore: cerramos tus sesiones por seguridad";

        var html = Wrap(
            subject,
            "Detectamos el reuso de una credencial de sesion ya rotada.",
            "Cerramos tus sesiones por seguridad",
            $"""
             {Paragraph($"Hola {Encode(name)},")}
             {Paragraph("Detectamos un intento de reutilizar una credencial de sesion que ya habia sido rotada. Suele significar que alguien copio la sesion de tu navegador.")}
             {Paragraph("Por precaucion <strong>cerramos todas tus sesiones abiertas</strong>. Puedes volver a entrar con normalidad.")}
             {Button(panelUrl, "Entrar al panel")}
             {Notice("Si no reconoces esta actividad, cambia tu contrase&ntilde;a en cuanto entres y revisa tus API Keys.")}
             """);

        var text = $"""
            Hola {name},

            Detectamos un intento de reutilizar una credencial de sesion que ya
            habia sido rotada. Por precaucion cerramos todas tus sesiones abiertas.

            Vuelve a entrar en {panelUrl} con normalidad.

            Si no reconoces esta actividad, cambia tu contrasena en cuanto entres
            y revisa tus API Keys.
            """;

        return new EmailMessage(to, subject, html, text);
    }

    public static EmailMessage ApiKeyActivity(
        string to,
        string name,
        string keyName,
        string prefix,
        bool rotated,
        string panelUrl)
    {
        var action = rotated ? "roto" : "creado";
        var subject = $"FileStore: se ha {action} una API Key";

        var html = Wrap(
            subject,
            $"La API Key \"{Encode(keyName)}\" se ha {action} en tu cuenta.",
            $"Se ha {action} una API Key",
            $"""
             {Paragraph($"Hola {Encode(name)},")}
             {Paragraph($"Se {action} una API Key en tu cuenta:")}
             {CredentialBox(Encode(keyName), Encode(prefix))}
             {Notice("Si no fuiste tu, entra al panel y revocala cuanto antes: una API Key da acceso completo a tus archivos.")}
             {Button(panelUrl, "Revisar mis API Keys")}
             """);

        var text = $"""
            Hola {name},

            Se {action} la API Key "{keyName}" ({prefix}) en tu cuenta.

            Si fuiste tu, no hay nada que hacer. Si no, entra al panel y revocala
            cuanto antes: una API Key da acceso completo a tus archivos.

            {panelUrl}
            """;

        return new EmailMessage(to, subject, html, text);
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    /// <summary>
    /// Barra de consumo. Se dibuja con dos celdas de tabla y no con un div de
    /// ancho porcentual porque Outlook no respeta los porcentajes en divs.
    /// </summary>
    private static string ProgressBar(int percent, bool critical)
    {
        var filled = Math.Clamp(percent, 0, 100);
        var color = critical ? "#dc2626" : "#f59e0b";

        return $"""
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"
                   style="margin:20px 0;background-color:#e2e8f0;border-radius:5px;height:10px;">
              <tr>
                <td width="{filled}%" style="background-color:{color};border-radius:5px;
                                             height:10px;font-size:0;line-height:0;">&nbsp;</td>
                <td style="font-size:0;line-height:0;">&nbsp;</td>
              </tr>
            </table>
            """;
    }

    /// <summary>
    /// Tamaños legibles para el correo. Se duplica la logica del pipe del panel a
    /// proposito: Application no puede depender del frontend, y son cuatro lineas.
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
