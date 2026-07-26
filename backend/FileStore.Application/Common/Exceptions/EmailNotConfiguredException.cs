namespace FileStore.Application.Common.Exceptions;

/// <summary>
/// La operacion necesita entregar un correo y no hay proveedor configurado.
///
/// Existe para fallar rapido en vez de dejar un destrozo silencioso: dar de alta
/// un cliente genera una contraseña que solo viaja por correo, no la devuelve la
/// API y no se registra en el log. Sin envio real, esa cuenta queda inaccesible
/// para siempre salvo escribiendo un hash a mano en la base. Un reseteo es peor
/// todavia: destruye una contraseña que funcionaba.
///
/// Se mapea a 503 y no a 500: el servicio esta sano, le falta configuracion, y
/// la misma peticion va a funcionar en cuanto se complete.
/// </summary>
public class EmailNotConfiguredException(string operation)
    : Exception(
        $"No se puede {operation}: el envio de correo no esta configurado y la " +
        "contraseña solo se entrega por ese canal. Configura Resend:ApiKey y " +
        "Resend:FromAddress (ver EMAIL.md) y reintenta.");
