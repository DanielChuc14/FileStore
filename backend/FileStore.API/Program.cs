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
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Serilog;

const string AngularDevCorsPolicy = "AngularDev";

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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

// Seed idempotente del super-admin. Las migraciones NO se aplican aqui a
// proposito: `Database.Migrate()` en el arranque significa que un deploy puede
// alterar el esquema sin que nadie lo haya decidido. Se aplican con
// `dotnet ef database update` como paso explicito.
await using (var scope = app.Services.CreateAsyncScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    await seeder.SeedAsync();
}

// Serilog va PRIMERO para que quede por fuera del manejador de excepciones y
// registre el codigo que realmente recibe el cliente. Al reves, una excepcion
// mapeada a 409 o 404 se loguearia como 500, porque Serilog la veria antes de
// que el handler reescriba la respuesta.
app.UseSerilogRequestLogging();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
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
