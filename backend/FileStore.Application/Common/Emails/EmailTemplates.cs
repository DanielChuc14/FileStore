using System.Net;
using FileStore.Application.Abstractions;

namespace FileStore.Application.Common.Emails;

/// <summary>
/// Plantillas de los correos transaccionales.
///
/// Son funciones puras que devuelven el mensaje ya armado: sin motor de
/// plantillas ni dependencias, porque son pocas y cortas. Si algun dia crecen o
/// hay que traducirlas, el punto de cambio es este y nada mas.
///
/// Todo dato que venga del usuario (nombre del cliente, sobre todo) pasa por
/// HtmlEncode: un cliente llamado &lt;script&gt; no puede inyectar nada en el
/// correo que recibe otra persona.
/// </summary>
public static class EmailTemplates
{
    public static EmailMessage Welcome(string to, string name, string password, string panelUrl)
    {
        var safeName = WebUtility.HtmlEncode(name);

        var html = Layout(
            "Tu cuenta de FileStore esta lista",
            $"""
             <p>Hola {safeName},</p>
             <p>Se creo tu cuenta en FileStore. Estas son tus credenciales de acceso:</p>
             <p style="margin:24px 0">
               <strong>Usuario:</strong> {WebUtility.HtmlEncode(to)}<br>
               <strong>Contrase&ntilde;a:</strong> <code>{WebUtility.HtmlEncode(password)}</code>
             </p>
             <p>Entra en <a href="{panelUrl}">{panelUrl}</a> y cambiala en cuanto puedas.</p>
             <p>Si no esperabas este correo, ignoralo y avisa a quien administra el servicio.</p>
             """);

        var text = $"""
            Hola {name},

            Se creo tu cuenta en FileStore.

            Usuario: {to}
            Contrasena: {password}

            Entra en {panelUrl} y cambiala en cuanto puedas.

            Si no esperabas este correo, ignoralo y avisa a quien administra el servicio.
            """;

        return new EmailMessage(to, "Tu cuenta de FileStore esta lista", html, text);
    }

    public static EmailMessage PasswordReset(string to, string name, string password, string panelUrl)
    {
        var safeName = WebUtility.HtmlEncode(name);

        var html = Layout(
            "Se restablecio tu contrase&ntilde;a",
            $"""
             <p>Hola {safeName},</p>
             <p>Quien administra el servicio restablecio tu contrase&ntilde;a. La nueva es:</p>
             <p style="margin:24px 0"><code>{WebUtility.HtmlEncode(password)}</code></p>
             <p>Entra en <a href="{panelUrl}">{panelUrl}</a> y cambiala en cuanto puedas.
                Todas tus sesiones abiertas se cerraron.</p>
             <p>Si no pediste este cambio, avisa cuanto antes: puede indicar que
                alguien accedio a tu cuenta.</p>
             """);

        // Se etiqueta igual que en el correo de alta: mismo formato en los dos
        // sitios donde viaja una contraseña.
        var text = $"""
            Hola {name},

            Quien administra el servicio restablecio tu contrasena.

            Contrasena: {password}

            Entra en {panelUrl} y cambiala en cuanto puedas. Todas tus sesiones
            abiertas se cerraron.

            Si no pediste este cambio, avisa cuanto antes.
            """;

        return new EmailMessage(to, "Se restablecio tu contrasena de FileStore", html, text);
    }

    public static EmailMessage PasswordResetLink(
        string to,
        string name,
        string resetUrl,
        int minutesValid)
    {
        var safeName = WebUtility.HtmlEncode(name);

        var html = Layout(
            "Recupera tu contrase&ntilde;a",
            $"""
             <p>Hola {safeName},</p>
             <p>Pediste recuperar el acceso a tu cuenta de FileStore. Este enlace
                sirve <strong>una sola vez</strong> y vence en {minutesValid} minutos:</p>
             <p style="margin:24px 0">
               <a href="{resetUrl}"
                  style="display:inline-block;background:#0f172a;color:#fff;
                         padding:10px 18px;border-radius:6px;text-decoration:none">
                 Elegir una contrase&ntilde;a nueva
               </a>
             </p>
             <p style="font-size:13px;color:#6b7280">Si el boton no funciona, copia esta direccion:<br>
                <span style="word-break:break-all">{resetUrl}</span></p>
             <p><strong>Si no pediste esto</strong>, ignora el correo: tu contrase&ntilde;a
                sigue igual y el enlace vence solo.</p>
             """);

        var text = $"""
            Hola {name},

            Pediste recuperar el acceso a tu cuenta de FileStore. Este enlace sirve
            una sola vez y vence en {minutesValid} minutos:

            {resetUrl}

            Si no pediste esto, ignora el correo: tu contrasena sigue igual y el
            enlace vence solo.
            """;

        return new EmailMessage(to, "Recupera tu contrasena de FileStore", html, text);
    }

    public static EmailMessage QuotaAlert(
        string to,
        string name,
        int percent,
        long usedBytes,
        long quotaBytes,
        string panelUrl)
    {
        var safeName = WebUtility.HtmlEncode(name);
        var used = FormatBytes(usedBytes);
        var quota = FormatBytes(quotaBytes);

        // Al 95% el mensaje cambia de tono: ya no es informativo, es inminente.
        var critical = percent >= 95;
        var heading = critical
            ? "Tu espacio esta por agotarse"
            : "Tu espacio esta al " + percent + "%";

        var advice = critical
            ? "Cuando se llene, las subidas empezaran a fallar."
            : "Todavia tienes margen, pero conviene que lo revises.";

        var html = Layout(
            heading,
            $"""
             <p>Hola {safeName},</p>
             <p>Estas usando <strong>{used}</strong> de <strong>{quota}</strong>
                ({percent}%). {advice}</p>
             <p>Recuerda que la papelera y las versiones antiguas <strong>tambien
                ocupan cuota</strong>: vaciar la papelera desde el panel suele
                liberar bastante.</p>
             <p><a href="{panelUrl}">Ir al panel</a></p>
             """);

        var text = $"""
            Hola {name},

            Estas usando {used} de {quota} ({percent}%). {advice}

            La papelera y las versiones antiguas tambien ocupan cuota: vaciar la
            papelera desde el panel suele liberar bastante.

            {panelUrl}
            """;

        return new EmailMessage(to, $"FileStore: tu espacio esta al {percent}%", html, text);
    }

    public static EmailMessage SuspiciousSessionActivity(string to, string name, string panelUrl)
    {
        var safeName = WebUtility.HtmlEncode(name);

        var html = Layout(
            "Cerramos tus sesiones por seguridad",
            $"""
             <p>Hola {safeName},</p>
             <p>Detectamos un intento de reutilizar una credencial de sesion que ya
                habia sido rotada. Suele significar que alguien copio la sesion de
                tu navegador.</p>
             <p>Por precaucion <strong>cerramos todas tus sesiones abiertas</strong>.
                Vuelve a entrar en <a href="{panelUrl}">el panel</a> con normalidad.</p>
             <p>Si no reconoces esta actividad, cambia tu contrase&ntilde;a en cuanto
                entres y revisa tus API Keys.</p>
             """);

        var text = $"""
            Hola {name},

            Detectamos un intento de reutilizar una credencial de sesion que ya
            habia sido rotada. Por precaucion cerramos todas tus sesiones abiertas.

            Vuelve a entrar en {panelUrl} con normalidad.

            Si no reconoces esta actividad, cambia tu contrasena en cuanto entres
            y revisa tus API Keys.
            """;

        return new EmailMessage(to, "FileStore: cerramos tus sesiones por seguridad", html, text);
    }

    public static EmailMessage ApiKeyActivity(
        string to,
        string name,
        string keyName,
        string prefix,
        bool rotated,
        string panelUrl)
    {
        var safeName = WebUtility.HtmlEncode(name);
        var safeKey = WebUtility.HtmlEncode(keyName);
        var action = rotated ? "roto" : "creado";

        var html = Layout(
            $"Se ha {action} una API Key",
            $"""
             <p>Hola {safeName},</p>
             <p>Se {action} la API Key <strong>{safeKey}</strong>
                (<code>{WebUtility.HtmlEncode(prefix)}</code>) en tu cuenta.</p>
             <p>Si fuiste tu, no hay nada que hacer. Si no, entra al
                <a href="{panelUrl}">panel</a> y revocala cuanto antes: una API Key
                da acceso completo a tus archivos.</p>
             """);

        var text = $"""
            Hola {name},

            Se {action} la API Key "{keyName}" ({prefix}) en tu cuenta.

            Si fuiste tu, no hay nada que hacer. Si no, entra al panel y revocala
            cuanto antes: una API Key da acceso completo a tus archivos.

            {panelUrl}
            """;

        return new EmailMessage(to, $"FileStore: se ha {action} una API Key", html, text);
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

    /// <summary>
    /// Envoltura comun. Estilos en linea y tabla de una celda: los clientes de
    /// correo ignoran las hojas de estilo y buena parte del CSS moderno.
    /// </summary>
    private static string Layout(string heading, string body) =>
        $"""
         <div style="font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif;
                     font-size:15px;line-height:1.6;color:#1f2937;max-width:520px">
           <h1 style="font-size:18px;margin:0 0 16px">{heading}</h1>
           {body}
           <hr style="border:none;border-top:1px solid #e5e7eb;margin:32px 0">
           <p style="font-size:13px;color:#6b7280">
             FileStore &middot; Este es un mensaje automatico, no respondas a esta direccion.
           </p>
         </div>
         """;
}
