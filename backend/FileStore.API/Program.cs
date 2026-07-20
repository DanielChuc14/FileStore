using System.Text;
using FileStore.API.Infrastructure;
using FileStore.Application;
using FileStore.Application.Common;
using FileStore.Domain.Enums;
using FileStore.Infrastructure;
using FileStore.Infrastructure.Authentication;
using FileStore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthPolicies.SuperAdmin, policy =>
        policy.RequireRole(nameof(UserType.SuperAdmin)))
    .AddPolicy(AuthPolicies.Client, policy =>
        policy.RequireRole(nameof(UserType.Client)));

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

app.UseExceptionHandler();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors(AngularDevCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

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
