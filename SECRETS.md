# Configurar los secretos del backend (desde cero)

Esta guía es solo para **desarrollo local**. Para producción, ver `DEPLOYMENT.md`
y `.env.production.example` (esos secretos van en un archivo `.env`, no en
user-secrets).

## ¿Por qué user-secrets y no appsettings.json?

`appsettings.json` y `appsettings.Development.json` se commitean al repo, así
que ahí NUNCA van contraseñas ni claves. En su lugar, el backend usa
[`dotnet user-secrets`](https://learn.microsoft.com/aspnet/core/security/app-secrets),
una herramienta del SDK de .NET que guarda un JSON **fuera del repo** (en tu
carpeta de usuario) y que .NET combina automáticamente con la configuración
cuando corres el proyecto en modo `Development`.

El proyecto `FileStore.API` ya tiene un `UserSecretsId` fijo en su `.csproj`,
así que no hace falta correr `dotnet user-secrets init`: ya está vinculado a
un almacén de secretos.

## Opción rápida: script

```bash
./scripts/setup-dev-secrets.sh
```

Pregunta interactivamente por la conexión a Postgres y el super-admin, genera
solo el `Jwt:Secret` automáticamente, y configura los tres bloques de una
sola pasada. Es idempotente: si un secreto ya existe, lo deja igual (usar
`--force` para sobreescribir todo). Los pasos manuales de abajo son lo que
hace este script por debajo, por si preferís correrlos uno por uno o
entender qué hace cada uno.

## Requisitos previos

- .NET 10 SDK instalado (`dotnet --version`).
- PostgreSQL corriendo en local, con una base y un usuario ya creados
  (puede ser vía Docker con `docker-compose.yml`, o una instalación local).
- `openssl` disponible en la terminal (viene por defecto en Linux/macOS).

## Pasos

Todos los comandos se corren desde la raíz del repo. `--project` apunta al
proyecto de la API para que sepa en qué almacén de secretos guardar.

### 1. Cadena de conexión a Postgres

```bash
dotnet user-secrets set "ConnectionStrings:Default" \
  "Host=localhost;Port=5432;Database=filestore;Username=<tu_usuario>;Password=<tu_password>" \
  --project backend/FileStore.API
```

Reemplaza `<tu_usuario>` y `<tu_password>` por las credenciales reales de tu
Postgres local. El host/puerto/nombre de base cambian solo si tu instalación
es distinta a la default (`localhost:5432`, base `filestore`).

### 2. Secreto JWT

Tiene que pesar al menos 32 bytes (256 bits) porque se firma con HMAC-SHA256;
si es más corto, la API falla al arrancar. Se genera así:

```bash
dotnet user-secrets set "Jwt:Secret" "$(openssl rand -base64 48)" \
  --project backend/FileStore.API
```

Cada quien genera el suyo — no hay un valor "correcto" que compartir, solo
tiene que existir y cumplir el largo mínimo.

### 3. Super-admin inicial (opcional)

Si no se configura, el backend arranca igual pero no crea ningún super-admin
(no vas a poder loguearte hasta crear uno manualmente).

```bash
dotnet user-secrets set "SuperAdmin:Email" "admin@local" --project backend/FileStore.API
dotnet user-secrets set "SuperAdmin:Password" "<una_password>" --project backend/FileStore.API
dotnet user-secrets set "SuperAdmin:Name" "Super Admin" --project backend/FileStore.API
```

### 4. Correo con Resend (opcional)

Sin esto el backend arranca igual: los correos no se envían, se registran en el
log con un warning. Sirve para desarrollar sin cuenta de Resend.

Lo más cómodo es el script dedicado, que además valida y prueba el envío:

```bash
./scripts/setup-email-secrets.sh
```

A mano:

```bash
dotnet user-secrets set "Resend:ApiKey" "re_..." --project backend/FileStore.API
dotnet user-secrets set "Resend:FromAddress" "no-reply@tudominio.com" --project backend/FileStore.API
```

El dominio del remitente **tiene que estar verificado en Resend** (registros
SPF/DKIM en tu DNS) o la API rechaza cada envío. `Resend:FromName` y
`App:PanelUrl` tienen default y solo hace falta tocarlas si quieres otro nombre
visible u otra URL de panel.

Ojo: hacen falta **las dos** claves. Con solo una configurada, la app considera
que el correo no está listo y sigue usando el registro en log.

### 5. Verificar

```bash
dotnet user-secrets list --project backend/FileStore.API
```

Deberías ver algo como:

```
Jwt:Secret = ...
ConnectionStrings:Default = Host=localhost;Port=5432;Database=filestore;Username=...;Password=...
```

### 6. Correr el backend

```bash
dotnet run --project backend/FileStore.API
```

Si la base todavía no tiene tablas, primero aplica las migraciones:

```bash
dotnet ef database update --project backend/FileStore.Infrastructure --startup-project backend/FileStore.API
```

## Errores comunes

- **`Npgsql.PostgresException` / "Connection refused"**: Postgres no está
  corriendo, o el host/puerto/usuario/password de `ConnectionStrings:Default`
  no coinciden con tu instalación real.
- **`InvalidOperationException: 'Jwt:Secret' debe tener al menos 32 bytes`**:
  el secreto generado con `openssl rand -base64 48` es correcto; si lo pusiste
  a mano y quedó corto, hay que regenerarlo.
- **`Falta la seccion de configuracion 'Jwt'`**: no se configuró
  `Jwt:Secret` en absoluto — repetir el paso 2.
- **Los secretos "desaparecen"**: `dotnet user-secrets` los guarda por
  `UserSecretsId`, ligado a tu usuario del sistema operativo, no al repo. Si
  cambias de máquina o de usuario, hay que volver a correr estos comandos.

## Dónde vive esto físicamente

Linux/macOS: `~/.microsoft/usersecrets/<UserSecretsId>/secrets.json`
(el `UserSecretsId` de este proyecto está en
`backend/FileStore.API/FileStore.API.csproj`). No hace falta editarlo a mano;
usar siempre los comandos `dotnet user-secrets set/list/remove`.
