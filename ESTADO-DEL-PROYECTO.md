# FileStore — Estado del proyecto (handoff)

> Documento de contexto para retomar el trabajo en otra sesion. Resume que se
> hizo, que decisiones se tomaron, que falta validar y donde estan las trampas.
> Ultima actualizacion: 2026-07-25 (post-MVP: ampliacion de tests, documentacion
> de secretos y de la API, migracion del entorno local a Linux).

---

## 1. Que es

SaaS de gestion de archivos multi-cliente. Cada cliente tiene cuota fija, sube
archivos por API REST con API Keys, y se autogestiona desde un panel Angular con
JWT. Un super-admin crea clientes y configura el sistema. Almacenamiento en disco
local del servidor, metadatos en PostgreSQL.

El documento maestro de alcance es `FileStore-Definicion-Proyecto.md`.

---

## 2. Estado general

**Las 10 fases del plan estan completas**, y despues se hizo trabajo post-MVP
(seccion 2.1). Se trabaja directamente sobre `main`, todo commiteado y pusheado
a origin.

Las ramas `feature/scaffolding`, `test/core-flows-coverage` y
`test/purge-coverage` estan **fusionadas en main sin commits pendientes**: son
residuo y se pueden borrar del remoto sin perder nada.

| Fase | Que | Estado |
|---|---|---|
| 0 | Andamiaje Clean Architecture + entorno | Hecha y verificada |
| 1 | Modelo de dominio + persistencia | Hecha y verificada |
| 2 | Autenticacion JWT + login del panel | Hecha y verificada |
| 3 | Gestion de clientes (super-admin) + audit log | Hecha y verificada |
| 4 | API Keys + autenticacion de la API | Hecha y verificada |
| 5 | Carpetas y archivos (nucleo) | Hecha y verificada |
| 6 | Versionado, papelera, purga automatica | Hecha y verificada |
| 7 | Rate limiting, auditoria, dashboards, config | Hecha y verificada |
| 8 | Perfil, cambio de contraseña, cierre del MVP | Hecha y verificada |
| 9 | Testing (unit + integracion + Angular) | Hecha y verificada |
| 10 | Deployment (Docker, Nginx, checklist) | Escrita, **sin verificar** (no hay Docker local) |

### 2.1 Trabajo post-MVP

| Que | Estado |
|---|---|
| Ampliacion de tests a los flujos que solo se probaban a mano (papelera, versionado, path, refresh, rate limit, purga, config admin) | Hecha, fusionada |
| Refactor: logica de purga extraida a `ITrashPurger` para poder testearla | Hecha |
| `SECRETS.md` + `scripts/setup-dev-secrets.sh`: setup de user-secrets desde cero | Hecha |
| `API.md`: guia de integracion para quien consume la API | Hecha |
| Migracion del entorno local de Windows a Fedora | Hecha |
| Cierre de huecos de test Tier 1 + 2 bugs encontrados (2026-07-26) | Hecha |
| Correo transaccional con Resend: outbox, 6 plantillas, recuperacion autoservicio, avisos de cuota y alertas de seguridad | Hecha, **sin enviar** (falta verificar el dominio) |

---

## 3. Stack y decisiones clave

- **Backend**: .NET 10, Clean Architecture en 4 proyectos (Domain, Application,
  Infrastructure, API) + CQRS con MediatR. EF Core + Npgsql. FluentValidation.
  Serilog. Autenticacion JWT + esquema propio de API Key.
- **Frontend**: Angular 21 (standalone components, signals), Tailwind v4,
  ngx-translate (i18n desde el inicio, solo español por ahora), Chart.js.
- **Base**: PostgreSQL 16.
- **Tests**: xUnit (unit + integracion con WebApplicationFactory), vitest (Angular).

### Decisiones que conviene recordar

- **MediatR fijado en 12.5.0**: las versiones 13+ pasaron a licencia RPL 1.5
  (copeleft de red) o comercial. 12.5.0 es la ultima Apache-2.0. NO actualizar.
- **ApexCharts y FluentAssertions descartadas** por el mismo motivo (licencia
  dual/comercial). Se usa Chart.js (MIT) y xUnit Assert puro.
- **Papelera SI cuenta en la cuota** (decision 1 del documento). Borrar es soft
  delete; el archivo sigue ocupando cuota hasta purgarse.
- **Cifrado en reposo a nivel de disco** (LUKS), no a nivel de aplicacion.
  `IStorageService` opera sobre Stream para admitir un decorador de cifrado a
  futuro sin tocar handlers.
- **Refresh token en cookie httpOnly** (no localStorage), con rotacion y
  deteccion de reutilizacion. Access token solo en memoria del frontend.
- **`/files` y `/folders` aceptan JWT de Client O API Key** (autenticacion dual):
  el panel y una integracion consumen los mismos endpoints. El super-admin queda
  afuera (no tiene claim client_id).
- **Aislamiento por ClientId**: siempre en el WHERE, nunca en un chequeo
  posterior. Pedir recurso ajeno devuelve 404, no 403 (no confirma que existe).
- **Deployment target: VPS Linux propio** con Docker Compose + Nginx + Let's Encrypt.
- **Fase 9 (testing) se hizo al final**, no fase por fase, por decision del usuario.
- **Notificaciones por email: fuera del MVP** (el dashboard avisa visualmente al
  80/95% de cuota). Retomar como feature post-MVP si se quiere SMTP.

---

## 4. Entorno local

### Correr la app

```
# Terminal 1 (raiz del repo). Usa el perfil https por defecto.
dotnet run --project backend/FileStore.API

# Terminal 2
cd frontend
ng serve
```

Panel: **http://localhost:4200** (NO el 7249, que es la API directa).
API: https://localhost:7249 y http://localhost:5263.
Swagger: **http://localhost:5263/swagger** (http, sin lio de certificado).

El orden importa: arrancar la API primero, o el proxy del frontend da ECONNREFUSED.

### Base de datos

Entorno actual: **Fedora Linux**. PostgreSQL 16 nativo como servicio del sistema
(NO Docker en local), `psql` en el PATH.

```bash
systemctl status postgresql      # el servicio corre en 127.0.0.1:5432
```

- Base de la app: `filestore`, rol `dev_user` (no superusuario).
- Base de tests: `filestore_test`, rol `filestore_test` (password descartable
  `test_local_only`, esta en el repo a proposito porque solo accede a esa base).
  **Ya creada en la maquina Linux (2026-07-26)** y con el esquema migrado. Ver
  `backend/tests/README.md`. Ojo al recrearla: `dev_user` tiene `CREATEDB` pero
  no `CREATEROLE`, asi que el `CREATE ROLE` necesita el superusuario `postgres`.

### Secretos (user-secrets, fuera del repo)

La cadena de conexion, el `Jwt:Secret` y las credenciales del super-admin viven
en user-secrets del proyecto API, NO en el repo. Un clon nuevo necesita
reconfigurarlos:

```bash
./scripts/setup-dev-secrets.sh              # interactivo e idempotente
dotnet user-secrets list --project backend/FileStore.API
```

Guia completa, incluidos los errores comunes, en **`SECRETS.md`**.

Claves que lee la app: `ConnectionStrings:Default` y `Jwt:Secret`
(obligatorias), `SuperAdmin:Email` / `SuperAdmin:Password` / `SuperAdmin:Name`
(opcionales: sin ellas el seeder no crea el super-admin y solo deja un warning
al arrancar). `Jwt:Issuer` y `Jwt:Audience` tienen default en `JwtSettings` y no
hace falta configurarlas.

### Credenciales de desarrollo

- **Super-admin**: el email y la contraseña son los que se hayan configurado en
  user-secrets (`dotnet user-secrets list`). No se pueden recuperar del hash; si
  se pierden, borrar la fila de `SuperAdmins` y reiniciar la API para que el
  seeder la vuelva a crear.
- **Clientes demo**: con `Seed:Demo` activo y la base sin clientes se siembran
  `demo-acme@filestore.local`, `demo-beta@filestore.local` y
  `demo-gamma@filestore.local`. Contraseña comun `Demo1234!` (override con
  `Seed:DemoPassword`). Las API Keys completas se escriben en el log al arrancar,
  a proposito, para poder probar la API con curl.

---

## 5. Estructura del repo

```
backend/
  FileStore.Domain/          entidades y enums, sin dependencias
  FileStore.Application/      CQRS (features), abstracciones, validadores
  FileStore.Infrastructure/   EF, storage, auth, background jobs, servicios
  FileStore.API/              controllers, Program.cs, middleware
  tests/
    FileStore.UnitTests/        logica pura, sin base ni red
    FileStore.IntegrationTests/ API real + Postgres
    README.md                   setup de la base de tests
frontend/
  src/app/
    core/       servicios (auth, clients, api-keys, files, stats)
    features/   vistas (login, dashboard, files, trash, api-keys, audit, admin, profile)
    layout/     shell con menu por rol
    shared/     pipe de bytes, componente de grafica
  nginx.conf, Dockerfile
scripts/setup-dev-secrets.sh       setup de user-secrets, idempotente
docker-compose.prod.yml, .env.production.example, DEPLOYMENT.md
backend/Dockerfile
FileStore-Definicion-Proyecto.md   documento maestro
API.md                             guia de integracion para consumidores
SECRETS.md                         secretos en desarrollo
```

---

## 6. Tests: que cubren y que no

La cobertura crecio bastante despues del MVP: las areas que antes solo se habian
probado a mano ya tienen tests automaticos.

- **70 unitarios** (<1 s): reglas de nombres (path traversal, caracteres
  prohibidos, reservados de Windows), generadores de contraseña y API Key,
  hashing, validadores de comandos, PagedResult. **Verificados en verde
  (2026-07-25).**
- **22 de Angular** (vitest, ~2 s): pipe de bytes, login, guards e interceptor de
  auth. **Verificados en verde (2026-07-25).**
- **De integracion**: autenticacion, **aislamiento entre clientes por JWT y API
  Key**, upload, versionado, extension no permitida, **cuota concurrente**,
  borrado suave, descarga byte a byte, papelera, carpetas, purga, rate limiting,
  refresh token y config de admin. Un archivo por area:

  ```
  AuthTests  IsolationTests  FileOperationsTests  VersioningTests  TrashTests
  FolderTests  PurgeTests  RateLimitTests  RefreshTokenTests  AdminConfigTests
  FolderDeleteTests  ClientLifecycleTests  ApiKeyRotationTests  AuditTests
  ClientEmailTests  PasswordRecoveryTests  NotificationTests  EmailDispatcherTests
  EmailNotConfiguredTests  ClientQuotaTests  FileUpdateTests  AccountTests
  ```

  **128 tests, verificados en verde en Linux (2026-07-26, 33 s).** Crecieron de
  44 a 87 ese mismo dia: primero al cerrar los huecos de mayor riesgo (borrado
  recursivo de carpetas, baja de cliente con token vigente, rotacion de API Key
  y auditoria, que no tenia ni un assert), y despues con el correo transaccional
  (credenciales por email, recuperacion autoservicio y avisos automaticos).

### Correr los tests

```bash
dotnet test backend/tests/FileStore.UnitTests           # sin preparacion
dotnet test backend/tests/FileStore.IntegrationTests    # necesita filestore_test
cd frontend && npm test
```

Setup de la base de tests documentado en `backend/tests/README.md`.

### Lo que sigue sin cubrir

- Flujo de deployment completo (Docker, Nginx, TLS): no hay tests y no se
  ejecuto nunca. Ver seccion 7.
- Pruebas end-to-end manuales del sistema terminado, de punta a punta.
- **Endpoints sin tocar por ningun test**: quedan 3 de 43 (empezo el dia en 24
  de 41). Los tres son de solo lectura y sin logica propia: `GET /me/stats`,
  `GET /admin/clients/{id}` y `GET /admin/whoami`.
- Frontend: los specs cubren el pipe de bytes, login, guards e interceptor. Sin
  cobertura los cuatro servicios de `core/` y los nueve componentes de feature.

Se priorizo riesgo (aislamiento, cuota, credenciales) sobre numero de lineas.

---

## 7. Pendiente por VALIDAR

1. **Fase 10 completa (deployment)**: nunca se ejecuto `docker build` ni
   `docker compose up` porque no hay Docker disponible en local (en la maquina
   Linux actual el usuario no esta en el grupo `docker`). Hay que probarlo en el
   VPS. Lo verificado: el `npm run build` de produccion genera
   `dist/frontend/browser` ok, el backend compila, y los tests pasan.
2. **Swap en el VPS**: si el VPS tiene 2 GB, el `ng build` puede morir por falta
   de RAM. Conviene agregar 2-4 GB de swap antes del primer build. Confirmado que
   **sigue sin estar documentado en DEPLOYMENT.md**.
3. **Pruebas end-to-end completas** del sistema terminado: el usuario queria
   hacer una pasada final de todo junto para detectar cualquier cosa. No se hizo.

Resuelto el 2026-07-26: los tests de integracion en Linux. Se creo la base y el
rol `filestore_test` y la suite completa (44) quedo en verde junto con los 70
unitarios.

---

## 8. Gotchas y lecciones (bugs recurrentes que reaparecen)

- **Referencia circular StoredFile <-> FileVersion**: se apuntan por FK
  (FileId / CurrentVersionId con NoAction). Al insertar o al hard delete hay que
  partir en dos SaveChanges dentro de una transaccion, o soltar CurrentVersionId
  antes de borrar versiones. Reaparecio en Fase 5 (upload) y Fase 6 (hard delete).
- **Trampas de licencia**: verificar SIEMPRE la licencia antes de instalar un
  paquete. Ya cayeron MediatR, ApexCharts y FluentAssertions (todas pasaron a
  dual/comercial). Patron: "SEE LICENSE IN LICENSE" en el nuspec/npm.
- **Chequeos de seguridad repartidos por handler se olvidan**. La validacion de
  "cliente activo y no dado de baja" estaba solo en Upload, GetProfile y
  ChangePassword: los demas handlers no la hacian, asi que un cliente dado de
  baja seguia listando y descargando con su access token hasta que expirara (15
  min). El canal de API Key nunca tuvo el problema porque lo valida al
  autenticar, en UN solo sitio. Se resolvio igual: `ClientStatusBehavior` en el
  pipeline de MediatR. Si aparece otro chequeo transversal, va ahi, no en cada
  handler. Ojo tambien con el razonamiento que lo tapaba: revocar refresh tokens
  NO invalida un access token ya emitido, solo impide renovarlo.
- **ActorType.ApiKey no se escribia nunca**: `AuditLogger` decidia el actor
  mirando `UserType`, y el esquema de API Key no emite ese claim, asi que toda
  accion de una integracion se auditaba como `Client`. El canal se decide por
  `ApiKeyId`, no por `UserType`. Leccion general: un valor de enum que ningun
  test ejercita puede llevar meses muerto sin que nada falle.
- **Interceptor de 401 en Angular**: no todo 401 es sesion vencida. El cambio de
  contraseña devuelve 401 por "contraseña actual incorrecta" y NO debe disparar
  el refresh ni desloguear. Los endpoints que validan credenciales se excluyen.
- **ForwardedHeaders en produccion**: detras de Nginx la API ve HTTP y la IP del
  proxy. Sin ForwardedHeaders el audit log guarda la IP equivocada y el redirect
  a HTTPS entra en loop. Ya esta resuelto en Program.cs (solo fuera de Development).
- **UseStatusCodePages**: el 401 y 403 los emite el middleware de auth, no el
  exception handler, y salian con cuerpo vacio. Se agrego para que devuelvan
  Problem Details como el resto.
- **Health checks**: `/health` es liveness (no consulta la BD), `/health/ready`
  es readiness (si consulta). No mezclar, o el orquestador reinicia el contenedor
  cada vez que la BD parpadea.
- **Swagger en dev**: `UseHttpsRedirection` rompia Swagger UI (redirect al puerto
  con certificado autofirmado). Se desactivo en Development; se mantiene fuera.
- **min-w-0 en flex**: `truncate` no funciona en un hijo flex sin `min-w-0`.
  Causo el desborde del MIME de docx en la config.

---

## 8.1 Correo transaccional

Montado el 2026-07-26 y documentado en **`EMAIL.md`**: outbox transaccional,
despachador con reintentos, seis plantillas, recuperacion autoservicio de
contraseña, avisos de cuota al 80/95% y alertas de seguridad.

**No sale ni un correo todavia.** Falta que el dominio quede verificado en
Resend (estaba en "checking DNS") y configurar `Resend:ApiKey` y
`Resend:FromAddress`. Sin las dos claves la app usa `LoggingEmailSender` y solo
registra en el log. El checklist de activacion esta en `EMAIL.md`.

Cambio incompatible que trajo: **el alta de cliente ya no devuelve la contraseña
generada** (`POST /admin/clients` responde un ClientDto, y `reset-password`
responde 204). Va por correo directo al cliente. Antes el super-admin tenia que
hacersela llegar por un canal externo, normalmente un chat, y ese era el eslabon
mas debil de todo el sistema de credenciales.

---

## 9. Decisiones abiertas / roadmap

Del documento maestro (seccion 15) y lo que fue surgiendo:

- ~~Notificaciones por email~~ — **hecho** con Resend (ver seccion 8.1 y `EMAIL.md`).
  El dominio quedo verificado y el envio confirmado de punta a punta. El
  super-admin tambien tiene recuperacion por correo desde el 2026-07-26.
- Cifrado a nivel de aplicacion (AES-256-GCM por archivo) — roadmap; requiere
  gestion de claves fuera del servidor, streaming con GCM, decisiones sobre
  checksum y cuota. Descarta la deduplicacion por checksum.
- Antivirus scan (ClamAV) — fuera del MVP.
- Rol de BD no-superusuario en produccion (endurecimiento, documentado como
  opcional en DEPLOYMENT.md).
- Endurecer ForwardedHeaders con lista de proxies conocidos si el setup crece.

---

## 10. Como retomar

1. **En un clon nuevo**: correr `./scripts/setup-dev-secrets.sh` (o seguir
   `SECRETS.md`), aplicar migraciones y levantar la app (seccion 4).
2. Correr los tests (seccion 6) para confirmar que todo sigue verde. Si los de
   integracion fallan por conexion, revisar que exista `filestore_test` (seccion 4).
3. Si el objetivo es desplegar: seguir `DEPLOYMENT.md` en el VPS, con el pendiente
   del swap (seccion 7.2) en mente.
4. Si el objetivo es cerrar el MVP con mas confianza: hacer las pruebas end-to-end
   (seccion 7.3).
5. Si el objetivo es integrar la API desde otra app: `API.md` tiene el flujo
   completo, desde obtener una API Key hasta los ejemplos por lenguaje.
