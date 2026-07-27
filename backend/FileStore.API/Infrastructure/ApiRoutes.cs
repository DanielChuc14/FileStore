namespace FileStore.API.Infrastructure;

/// <summary>
/// Prefijo de version de la API.
///
/// La version va en la ruta (`/v1/files`) y no en una cabecera, que es el
/// enfoque mas extendido y el mas dificil de usar mal: se ve en cualquier log,
/// se prueba con un curl sin ceremonia, y quien integra no puede olvidarse de
/// mandarla, porque sin ella la URL sencillamente no existe. Una cabecera se
/// olvida y produce fallos confusos.
///
/// Versionar no es licencia para romper. Mientras alguien use v1, v1 se
/// mantiene. Solo aparece un v2 cuando haya un cambio incompatible que no se
/// pueda evitar, y entonces conviven hasta que los consumidores migren.
///
/// Se declara aqui y no literal en cada controller para que la version viva en
/// un unico sitio, pero cada controller sigue mostrando su ruta completa: leer
/// el archivo basta para saber a que responde.
///
/// Los health checks quedan FUERA a proposito: son infraestructura para el
/// orquestador, no parte del contrato con los consumidores, y no deberian
/// cambiar de ruta porque la API saque una version nueva.
/// </summary>
public static class ApiRoutes
{
    public const string V1 = "v1";
}
