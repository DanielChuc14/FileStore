# FileStore — Estado del proyecto (handoff)

> Documento de contexto para retomar el trabajo en otra sesion. Resume que se
> hizo, que decisiones se tomaron, que falta validar y donde estan las trampas.
> Ultima actualizacion: fin de la Fase 10.

---

## 1. Que es

SaaS de gestion de archivos multi-cliente. Cada cliente tiene cuota fija, sube
archivos por API REST con API Keys, y se autogestiona desde un panel Angular con
JWT. Un super-admin crea clientes y configura el sistema. Almacenamiento en disco
local del servidor, metadatos en PostgreSQL.

El documento maestro de alcance es `FileStore-Definicion-Proyecto.md`.
El plan de fases vive en `C:\Users\chucd\.claude\plans\haz-el-plan-dividido-iterative-kettle.md`.

---

## 2. Estado general

**Las 10 fases del plan estan completas.** Rama de trabajo: `feature/scaffolding`
(todo commiteado y pusheado a origin). Rama principal: `main`.

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

PostgreSQL 16 nativo (NO Docker en local; la maquina tiene 8 GB). `psql` esta en
`C:\Program Files\PostgreSQL\16\bin\psql.exe` (no en el PATH).

- Base de la app: `filestore`, rol `filestore` (no superusuario, acotado a esa base).
- Base de tests: `filestore_test`, rol `filestore_test` (password descartable
  `test_local_only`, esta en el repo a proposito porque solo accede a esa base).

### Secretos (user-secrets, fuera del repo)

La cadena de conexion, el `Jwt:Secret` y las credenciales del super-admin viven
en user-secrets del proyecto API, NO en el repo:

```
dotnet user-secrets list --project backend/FileStore.API
```

Un clon nuevo del repo necesita reconfigurarlos. Claves: `ConnectionStrings:Default`,
`Jwt:Secret`, `Jwt:Issuer`, `Jwt:Audience`, `SuperAdmin:Email`,
`SuperAdmin:Password`, `SuperAdmin:Name`.

### Credenciales de desarrollo

- **Super-admin**: email `eduardo.chuc.dev@gmail.com`. La contraseña esta en
  user-secrets (`dotnet user-secrets list`). No se puede recuperar del hash.
- **Cliente de prueba**: `chrome.fase6@example.com`. La contraseña se fue
  reseteando durante las validaciones; si hace falta, resetearla desde el panel
  del super-admin (Clientes -> Resetear contraseña) o crear un cliente nuevo.
- Hay ~15-20 clientes basura de las verificaciones (nombres "Files A", "Iso B",
  etc.). No molestan; se pueden limpiar si se quiere.

---

## 5. Estructura del repo

```
backend/
  FileStore.Domain/          entidades y enums, sin dependencias
  FileStore.Application/      CQRS (features), abstracciones, validadores
  FileStore.Infrastructure/   EF, storage, auth, background jobs, servicios
  FileStore.API/              controllers, Program.cs, middleware
  tests/
    FileStore.UnitTests/       67 tests de logica pura
    FileStore.IntegrationTests/ 22 tests con API real + Postgres
frontend/
  src/app/
    core/       servicios (auth, clients, api-keys, files, stats)
    features/   vistas (login, dashboard, files, trash, api-keys, audit, admin, profile)
    layout/     shell con menu por rol
    shared/     pipe de bytes, componente de grafica
  nginx.conf, Dockerfile
docker-compose.prod.yml, .env.production.example, DEPLOYMENT.md
backend/Dockerfile
FileStore-Definicion-Proyecto.md   documento maestro
```

---

## 6. Tests: que cubren y que no

101 tests en total, todos verdes.

- **67 unitarios** (<1 s): reglas de nombres (path traversal, caracteres
  prohibidos, reservados de Windows), generadores de contraseña y API Key,
  hashing, validadores de comandos, PagedResult.
- **22 de integracion** (~8-14 s): autenticacion, **aislamiento entre clientes
  por JWT y API Key**, upload, versionado, extension no permitida, **cuota
  concurrente** (6 subidas en paralelo), borrado suave, descarga byte a byte.
- **12 de Angular**: pipe de bytes, comportamiento del login (mock del servicio).

### Correr los tests

```
dotnet test backend/tests/FileStore.UnitTests           # sin preparacion
dotnet test backend/tests/FileStore.IntegrationTests    # necesita filestore_test
cd frontend && npm test
```

Setup de la base de tests documentado en `backend/tests/README.md`.

### NO cubierto por tests automaticos (verificado a mano en su fase, sin red permanente)

- Job de purga de papelera (se apaga en los tests).
- Rate limiting (429 con Retry-After).
- Rotacion de refresh tokens y deteccion de reutilizacion.
- Mover/renombrar carpetas con recalculo de path en la descendencia.
- Restaurar de papelera y hard delete.
- Endpoints de estadisticas y configuracion del admin.

Cobertura por lineas estimada ~40-50%. Se priorizo riesgo (aislamiento, cuota),
no numero. Ampliar estas areas es una tarea pendiente opcional.

---

## 7. Pendiente por VALIDAR

1. **Fase 10 completa (deployment)**: nunca se ejecuto `docker build` ni
   `docker compose up` porque no hay Docker local. Hay que probarlo en el VPS.
   Lo verificado: el `npm run build` de produccion genera `dist/frontend/browser`
   ok, el backend compila, y los tests pasan con los cambios de produccion.
2. **Swap en el VPS**: si el VPS tiene 2 GB, el `ng build` puede morir por falta
   de RAM. Conviene agregar 2-4 GB de swap antes del primer build. NO esta
   documentado aun en DEPLOYMENT.md (quedo pendiente de agregar).
3. **Pruebas end-to-end completas** del sistema terminado: el usuario queria
   hacer una pasada final de todo junto para detectar cualquier cosa. No se hizo.

---

## 8. Gotchas y lecciones (bugs recurrentes que reaparecen)

- **Referencia circular StoredFile <-> FileVersion**: se apuntan por FK
  (FileId / CurrentVersionId con NoAction). Al insertar o al hard delete hay que
  partir en dos SaveChanges dentro de una transaccion, o soltar CurrentVersionId
  antes de borrar versiones. Reaparecio en Fase 5 (upload) y Fase 6 (hard delete).
- **PowerShell 5.1**: no tiene `Invoke-WebRequest -Form` (usar curl.exe para
  multipart), se come las comillas internas al pasar JSON a curl (usar archivos
  `-d @archivo.json`), y `-o $null` escribe a un archivo "null" (usar un sink real).
  El contenedor de cookies descarta el header `Cookie` manual (usar el WebSession).
- **Trampas de licencia**: verificar SIEMPRE la licencia antes de instalar un
  paquete. Ya cayeron MediatR, ApexCharts y FluentAssertions (todas pasaron a
  dual/comercial). Patron: "SEE LICENSE IN LICENSE" en el nuspec/npm.
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

## 9. Decisiones abiertas / roadmap

Del documento maestro (seccion 15) y lo que fue surgiendo:

- Notificaciones por email (SMTP) — fuera del MVP.
- Cifrado a nivel de aplicacion (AES-256-GCM por archivo) — roadmap; requiere
  gestion de claves fuera del servidor, streaming con GCM, decisiones sobre
  checksum y cuota. Descarta la deduplicacion por checksum.
- Antivirus scan (ClamAV) — fuera del MVP.
- Ampliar cobertura de tests de integracion a las areas manuales (seccion 6).
- Rol de BD no-superusuario en produccion (endurecimiento, documentado como
  opcional en DEPLOYMENT.md).
- Endurecer ForwardedHeaders con lista de proxies conocidos si el setup crece.

---

## 10. Como retomar

1. Levantar la app (seccion 4) y correr los tests (seccion 6) para confirmar que
   todo sigue verde.
2. Si el objetivo es desplegar: seguir `DEPLOYMENT.md` en el VPS, con el pendiente
   del swap (seccion 7.2) en mente.
3. Si el objetivo es cerrar el MVP con mas confianza: hacer las pruebas end-to-end
   (seccion 7.3) y/o ampliar tests (seccion 6).
