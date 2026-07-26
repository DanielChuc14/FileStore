namespace FileStore.Application.Common.Emails;

/// <summary>
/// Piezas de maquetacion compartidas por todos los correos.
///
/// El HTML de correo no es HTML web: los clientes (Outlook sobre todo, que
/// renderiza con el motor de Word) ignoran las hojas de estilo externas, flexbox,
/// grid y buena parte del CSS moderno. De ahi las decisiones que parecen
/// anticuadas y no lo son:
///
/// - <b>Tablas para la estructura</b>, con role="presentation" para que los
///   lectores de pantalla no las anuncien como tablas de datos.
/// - <b>Estilos en linea</b>: es lo unico que respetan todos los clientes.
/// - <b>Ancho de 600 px</b>, el estandar que entra sin scroll horizontal en
///   practicamente cualquier cliente de escritorio.
/// - <b>Botones "a prueba de balas"</b>: una celda con bgcolor y un enlace con
///   padding dentro. Outlook ignora el padding de un enlace suelto, asi que un
///   boton hecho solo con &lt;a&gt; le sale como texto plano.
/// - <b>Sin imagenes remotas</b>: la mayoria de los clientes las bloquea por
///   defecto, asi que un logo en PNG se ve como un hueco roto. La marca es texto.
/// - <b>color-scheme: light</b>: sin esto, varios clientes invierten los colores
///   por su cuenta en modo oscuro y el resultado es impredecible. Es preferible
///   un correo claro y consistente que uno oscuro y roto.
/// </summary>
internal static class EmailLayout
{
    internal const string FontStack =
        "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

    private const string MonoStack =
        "ui-monospace,SFMono-Regular,Menlo,Consolas,'Liberation Mono',monospace";

    // Sin interpolar: las llaves del CSS son literales.
    private const string ResponsiveStyles = """
        <style>
          @media only screen and (max-width: 620px) {
            .fs-container { width: 100% !important; }
            .fs-pad { padding: 24px !important; }
          }
        </style>
        """;

    /// <summary>
    /// Envuelve el contenido en el documento completo.
    /// </summary>
    /// <param name="preheader">
    /// Texto de vista previa: lo que el buzon muestra junto al asunto antes de
    /// abrir el correo. Va oculto en el cuerpo. Sin el, el cliente muestra las
    /// primeras palabras que encuentre, que suelen ser "Hola Fulano".
    /// </param>
    internal static string Wrap(string title, string preheader, string heading, string body) =>
        $"""
         <!doctype html>
         <html lang="es">
         <head>
           <meta charset="utf-8">
           <meta name="viewport" content="width=device-width, initial-scale=1">
           <meta name="color-scheme" content="light">
           <meta name="supported-color-schemes" content="light">
           <title>{title}</title>
           {ResponsiveStyles}
         </head>
         <body style="margin:0;padding:0;background-color:#f1f5f9;">
           <div style="display:none;max-height:0;overflow:hidden;opacity:0;mso-hide:all;">
             {preheader}
           </div>

           <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"
                  style="background-color:#f1f5f9;">
             <tr>
               <td align="center" style="padding:32px 12px;">

                 <table role="presentation" class="fs-container" width="600" cellpadding="0" cellspacing="0"
                        border="0" style="width:600px;max-width:600px;background-color:#ffffff;
                                          border:1px solid #e2e8f0;border-radius:12px;">

                   <tr>
                     <td class="fs-pad" style="padding:24px 36px;border-bottom:1px solid #e2e8f0;">
                       <span style="font-family:{FontStack};font-size:16px;font-weight:700;
                                    color:#0f172a;letter-spacing:-0.2px;">FileStore</span>
                     </td>
                   </tr>

                   <tr>
                     <td class="fs-pad" style="padding:36px;font-family:{FontStack};font-size:15px;
                                               line-height:1.65;color:#334155;">
                       <h1 style="margin:0 0 20px;font-size:21px;line-height:1.3;font-weight:700;
                                  color:#0f172a;letter-spacing:-0.3px;">{heading}</h1>
                       {body}
                     </td>
                   </tr>

                   <tr>
                     <td class="fs-pad" style="padding:20px 36px;border-top:1px solid #e2e8f0;
                                               background-color:#f8fafc;border-radius:0 0 12px 12px;
                                               font-family:{FontStack};font-size:12px;line-height:1.6;
                                               color:#94a3b8;">
                       Mensaje automatico de FileStore. No respondas a esta direccion.
                     </td>
                   </tr>

                 </table>

               </td>
             </tr>
           </table>
         </body>
         </html>
         """;

    /// <summary>Boton que sobrevive a Outlook.</summary>
    internal static string Button(string url, string label) =>
        $"""
         <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:28px 0;">
           <tr>
             <td align="center" bgcolor="#0f172a" style="border-radius:8px;">
               <a href="{url}"
                  style="display:inline-block;padding:13px 28px;font-family:{FontStack};
                         font-size:15px;font-weight:600;color:#ffffff;text-decoration:none;
                         border-radius:8px;">{label}</a>
             </td>
           </tr>
         </table>
         """;

    /// <summary>
    /// Caja para una credencial. Monoespaciada y con buen espaciado entre
    /// caracteres, para que no se confundan 0/O ni 1/l al transcribirla a mano.
    /// </summary>
    internal static string CredentialBox(string label, string value) =>
        $"""
         <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"
                style="margin:24px 0;background-color:#f8fafc;border:1px solid #e2e8f0;
                       border-radius:8px;">
           <tr>
             <td style="padding:16px 20px;font-family:{FontStack};">
               <div style="font-size:12px;font-weight:600;text-transform:uppercase;
                           letter-spacing:0.6px;color:#94a3b8;margin-bottom:6px;">{label}</div>
               <div style="font-family:{MonoStack};font-size:16px;color:#0f172a;
                           word-break:break-all;letter-spacing:0.4px;">{value}</div>
             </td>
           </tr>
         </table>
         """;

    /// <summary>Aviso destacado, para lo que no puede pasar desapercibido.</summary>
    internal static string Notice(string html) =>
        $"""
         <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"
                style="margin:24px 0;background-color:#fffbeb;border-left:3px solid #f59e0b;
                       border-radius:0 6px 6px 0;">
           <tr>
             <td style="padding:14px 18px;font-family:{FontStack};font-size:14px;
                        line-height:1.6;color:#78350f;">{html}</td>
           </tr>
         </table>
         """;

    /// <summary>Texto secundario, para las notas al pie del contenido.</summary>
    internal static string Muted(string html) =>
        $"""<p style="margin:20px 0 0;font-size:13px;line-height:1.6;color:#94a3b8;">{html}</p>""";

    internal static string Paragraph(string html) =>
        $"""<p style="margin:0 0 14px;">{html}</p>""";
}
