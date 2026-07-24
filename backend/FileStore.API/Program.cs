using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using FileStore.API.Infrastructure;
using FileStore.Application;
using FileStore.Application.Abstractions;
using FileStore.Application.Common;
using FileStore.Domain.Enums;
using FileStore.Infrastructure;
using FileStore.Infrastructure.Authentication;
using FileStore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
// Microsoft.OpenApi 2.x (el que trae Swashbuckle 10) aplano el namespace:
// los tipos que antes vivian en Microsoft.OpenApi.Models estan ahora en la raiz.
using Microsoft.OpenApi;
using Serilog;

const string AngularDevCorsPolicy = "AngularDev";

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "FileStore API",
        Version = "v1",
        Description =
            "Servicio de gestion de archivos multi-cliente.\n\n" +
            "Dos canales de autenticacion:\n" +
            "- **JWT** para el panel (`/auth`, `/me`, `/admin`).\n" +
            "- **API Key** en el header `X-Api-Key` para integraciones.\n\n" +
            "Los endpoints de contenido (`/files`, `/folders`, `/trash`) aceptan ambos."
    });

    options.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Access token obtenido de POST /auth/login. Pegar solo el token."
    });

    options.AddSecurityDefinition(AuthSchemes.ApiKey, new OpenApiSecurityScheme
    {
        Name = AuthSchemes.ApiKeyHeader,
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "API Key completa, con el formato fs_live_XXXXXXXX.SECRETO"
    });

    // Sin esto, Swagger UI muestra los candados pero no adjunta las credenciales
    // al ejecutar, y todo responde 401.
    // En OpenApi 2.x las referencias dejaron de expresarse con la propiedad
    // Reference y pasaron a tener su propio tipo.
    // Swashbuckle 10 recibe una funcion sobre el documento, no el requisito ya
    // construido: la referencia necesita resolverse contra el documento final.
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme, document)] = [],
        [new OpenApiSecuritySchemeReference(AuthSchemes.ApiKey, document)] = []
    });
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// CurrentUser lee los claims del request en curso, asi que necesita acceso a
// HttpContext y vive por request.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// Lo usa el handler de API Keys para no escribir LastUsedAt en cada request.
builder.Services.AddMemoryCache();

var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
    ?? throw new InvalidOperationException("Falta la seccion de configuracion 'Jwt'.");

// El secreto firma los access tokens con HMAC-SHA256. Debajo de 256 bits (32
// bytes) la firma se debilita, y un secreto vacio por un error de despliegue
// firmaria con una clave nula sin avisar. Se valida al arranque para fallar
// rapido en vez de operar con tokens inseguros.
if (string.IsNullOrWhiteSpace(jwtSettings.Secret) ||
    Encoding.UTF8.GetByteCount(jwtSettings.Secret) < 32)
{
    throw new InvalidOperationException(
        "'Jwt:Secret' debe tener al menos 32 bytes (256 bits) para HMAC-SHA256.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Por defecto JwtBearer traduce los claims cortos a las URIs largas de
        // WS-Federation ("role" -> "http://schemas.microsoft.com/.../role").
        // Eso rompe el RoleClaimType configurado abajo: la politica buscaria
        // "role" y el claim ya se llamaria distinto. Se desactiva el mapeo.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSettings.Secret)),

            // Sin esto, .NET tolera 5 minutos de desfase por defecto: un token
            // de 15 minutos viviria 20.
            ClockSkew = TimeSpan.Zero,

            // Los claims se emiten con nombres cortos; hay que decirle al
            // validador cuales son, o [Authorize] por rol no encuentra nada.
            NameClaimType = AuthClaims.Email,
            RoleClaimType = AuthClaims.Role
        };
    })
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        AuthSchemes.ApiKey, _ => { });

// Cada politica declara explicitamente su esquema. Esto es lo que mantiene los
// dos canales separados: un JWT presentado a la API publica no autentica, y una
// API Key presentada al panel tampoco. Sin AddAuthenticationSchemes, cualquier
// principal autenticado por cualquier esquema podria satisfacer la politica.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthPolicies.SuperAdmin, policy => policy
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireRole(nameof(UserType.SuperAdmin)))
    .AddPolicy(AuthPolicies.Client, policy => policy
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireRole(nameof(UserType.Client)))
    .AddPolicy(AuthPolicies.ApiKey, policy => policy
        .AddAuthenticationSchemes(AuthSchemes.ApiKey)
        .RequireAuthenticatedUser())
    // Unica politica que admite los dos esquemas: el explorador del panel y una
    // integracion externa consumen exactamente los mismos endpoints. Exigir el
    // claim client_id excluye al super-admin, que administra cuentas pero no
    // accede al contenido de nadie.
    .AddPolicy(AuthPolicies.ClientContent, policy => policy
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, AuthSchemes.ApiKey)
        .RequireClaim(AuthClaims.ClientId));

// Rate limiting por API Key. Se usa el limitador incorporado de .NET en vez de
// uno propio: ya resuelve la ventana, la concurrencia y el rechazo.
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var apiKeyId = context.User.FindFirstValue(AuthClaims.ApiKeyId);

        // Sin API Key no hay limite: el panel se autentica con JWT y su uso lo
        // controla la sesion, no una cuota de peticiones.
        if (string.IsNullOrEmpty(apiKeyId))
        {
            return RateLimitPartition.GetNoLimiter("no-api-key");
        }

        var limit = int.TryParse(context.User.FindFirstValue(AuthClaims.RateLimit), out var parsed)
            ? parsed
            : 100;

        // Una particion por key: agotar una no afecta a las demas, ni siquiera
        // a las del mismo cliente.
        return RateLimitPartition.GetFixedWindowLimiter(apiKeyId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit,
            Window = TimeSpan.FromMinutes(1),

            // Sin cola: si se supera el limite se rechaza de inmediato. Encolar
            // haria que el cliente espere sin saber por que.
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
    });

    // Limite por IP para los endpoints de credenciales (/auth). El GlobalLimiter
    // de arriba solo frena el trafico con API Key; el login es anonimo y sin esto
    // no tendria ningun freno frente a fuerza bruta o credential stuffing.
    options.AddPolicy("auth", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString();

        // Sin IP identificable no se puede particionar por origen. En produccion,
        // detras de Nginx, ForwardedHeaders siempre recupera la IP real del
        // cliente; el unico caso sin IP es el host de test en memoria.
        if (string.IsNullOrEmpty(ip))
        {
            return RateLimitPartition.GetNoLimiter("no-ip");
        }

        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
    });

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        // Retry-After le dice al cliente cuanto esperar en vez de que reintente
        // a ciegas.
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var value)
            ? (int)value.TotalSeconds
            : 60;

        context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();

        await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Se supero el limite de peticiones por minuto.",
            Detail = $"Reintenta en {retryAfter} segundos.",
            Instance = context.HttpContext.Request.Path
        }, cancellationToken);
    };
});

builder.Services.AddCors(options =>
    options.AddPolicy(AngularDevCorsPolicy, policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
        .AllowAnyHeader()
        .AllowAnyMethod()
        // Necesario para que el navegador envie la cookie de refresh.
        // Es incompatible con AllowAnyOrigin, por eso los origenes son explicitos.
        .AllowCredentials()));

builder.Services
    .AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("Default")!,
        name: "postgres",
        tags: ["ready"]);

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    // Migracion opcional al arrancar. Por defecto NO corre: aplicar migraciones
    // en cada arranque significa que un deploy puede alterar el esquema sin que
    // nadie lo haya decidido. En un VPS de un solo operador se puede activar con
    // Database__ApplyMigrationsOnStartup=true para simplificar el primer deploy;
    // en un entorno con varios nodos conviene dejarlo apagado y migrar como paso
    // explicito para que no corran dos migraciones a la vez.
    if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
    {
        var context = scope.ServiceProvider.GetRequiredService<FileStoreDbContext>();
        await context.Database.MigrateAsync();
    }

    // Seed idempotente del super-admin.
    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    await seeder.SeedAsync();

    // Datos de demostracion, solo si Seed:Demo=true y la base no tiene clientes.
    var demoSeeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
    await demoSeeder.SeedAsync();
}

// En produccion la API corre detras de Nginx, que termina el TLS y le reenvia
// HTTP. Sin esto, la app veria el esquema http y la IP del contenedor Nginx: el
// redirect a HTTPS entraria en loop y el audit log registraria la IP del proxy
// en vez de la del cliente real. ForwardedHeaders lee X-Forwarded-Proto y
// X-Forwarded-For para recuperar el esquema y la IP originales. Va PRIMERO, antes
// de cualquier middleware que dependa de ellos.
if (!app.Environment.IsDevelopment())
{
    var forwardedOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    };

    // Solo se confia en el proxy que corre dentro de la red del compose. Se
    // limpian las listas por defecto y se declara unicamente esa subred (fijada
    // en docker-compose.prod.yml via ForwardedHeaders__KnownNetwork). Asi, si el
    // puerto de la API quedara expuesto, un X-Forwarded-For de un origen externo
    // no podria falsear la IP que queda en el audit log.
    forwardedOptions.KnownIPNetworks.Clear();
    forwardedOptions.KnownProxies.Clear();

    var knownNetwork = app.Configuration["ForwardedHeaders:KnownNetwork"];
    if (!string.IsNullOrWhiteSpace(knownNetwork))
    {
        forwardedOptions.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(knownNetwork));
    }

    app.UseForwardedHeaders(forwardedOptions);
}

// Serilog va PRIMERO para que quede por fuera del manejador de excepciones y
// registre el codigo que realmente recibe el cliente. Al reves, una excepcion
// mapeada a 409 o 404 se loguearia como 500, porque Serilog la veria antes de
// que el handler reescriba la respuesta.
app.UseSerilogRequestLogging();
app.UseExceptionHandler();

// El 401 y el 403 los emite el middleware de autenticacion, no el manejador de
// excepciones, y por defecto salen con el cuerpo vacio. Combinado con
// AddProblemDetails, esto les da el mismo formato que al resto de los errores.
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    // Solo se fuerza HTTPS fuera de desarrollo. En dev, el redirect 307 del
    // puerto http al https rompe Swagger UI: sus peticiones saltan al puerto
    // con el certificado autofirmado y el navegador las corta como error de
    // red. El panel Angular ya usa https via su proxy, asi que no lo necesita.
    app.UseHttpsRedirection();
}

app.UseCors(AngularDevCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

// Va DESPUES de la autorizacion, no solo de la autenticacion. UseAuthentication
// resuelve unicamente el esquema por defecto (JWT); el de API Key lo resuelve
// la autorizacion al evaluar la politica que lo declara. Colocado antes, el
// limitador no veria el claim de la key y no aplicaria ningun limite.
app.UseRateLimiter();

app.MapControllers();

// /health es liveness: no ejecuta ningun check, solo confirma que el proceso
// responde. Deliberadamente NO consulta la BD; si lo hiciera, una caida temporal
// de Postgres haria que el orquestador reinicie un contenedor que esta sano.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false
});

// /health/ready es readiness: verifica las dependencias. El orquestador lo usa
// para decidir si enrutar trafico, no para reiniciar.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();

// Con top-level statements, la clase Program generada es interna. Los tests de
// integracion la necesitan publica para WebApplicationFactory<Program>.
public partial class Program;
