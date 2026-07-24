# FileStore — Documento de Definición del Proyecto

> Documento maestro que consolida el alcance, decisiones y estructura del proyecto. Sirve como punto de partida para la documentación técnica detallada.

---

## 1. Resumen ejecutivo

**FileStore** es un servicio de gestión de archivos multi-cliente estilo SaaS. Cada cliente dispone de un espacio aislado con cuota fija, sube y consulta archivos vía **API REST** desde sus propios sistemas, y accede a un **panel web (Angular)** para auto-gestionar su cuenta, ver estadísticas de consumo, administrar API Keys y revisar auditoría. Un **super-administrador global** crea clientes, ajusta cuotas y monitorea el uso general del servicio.

El almacenamiento físico es local en el disco del servidor. La base de datos (PostgreSQL) guarda metadatos, usuarios, cuotas, versiones, auditoría y configuración.

---

## 2. Alcance funcional (MVP)

Entran en la primera versión:

- Gestión de clientes por parte del super-admin (alta, baja, ajuste de cuota, bloqueo)
- Autenticación dual: **API Keys** para consumo de API y **JWT** para el panel Angular
- Subida, descarga, listado, movimiento, renombrado y borrado de archivos vía API
- Organización en **carpetas jerárquicas**
- Cuota de almacenamiento fija configurable por cliente
- Whitelist de tipos de archivo (default definido, configurable por super-admin)
- Tamaño máximo por archivo configurable (default: 10 MB)
- **Versionado** de archivos (cada re-subida crea una versión nueva)
- **Soft delete + papelera** con auto-purga tras N días configurables
- **Audit log** completo de todas las operaciones
- **Rate limiting** por API Key configurable
- Panel Angular con dashboard, explorador de archivos, gestión de API Keys, auditoría y perfil
- Panel de super-admin con gestión de clientes, estadísticas globales y configuración del sistema

---

## 3. Actores y roles

**Super-administrador**: dueño operativo del servicio. Crea clientes, define cuotas, ajusta configuración global (tamaño máximo, tipos permitidos, días de purga), consulta métricas agregadas y puede bloquear cuentas. Se autentica con usuario/contraseña + JWT.

**Cliente**: cuenta individual con espacio propio y cuota fija. Se autentica en dos canales:
- En el **panel Angular** con email + contraseña (JWT).
- En la **API REST** con una o varias API Keys que él mismo administra desde el panel.

Un cliente no ve nada de otros clientes. El aislamiento es total en datos, almacenamiento físico y auditoría.

**Sistemas cliente (integraciones)**: aplicaciones que consumen la API usando una API Key del cliente. No son un actor humano, pero cada request queda registrado bajo la API Key correspondiente.

---

## 4. Autenticación y autorización

### API Keys (para la API REST)
- Cada cliente puede tener **múltiples API Keys**, cada una con nombre/descripción (ej: `prod`, `staging`, `mobile-app`).
- Las keys son **rotables**: se puede generar una nueva y revocar la anterior sin downtime.
- Se almacena solo el **hash** de la key; el valor completo se muestra una única vez al crearla.
- Cada key lleva un **prefijo visible** (ej: `fs_live_ab12…`) para identificarla en el panel sin comprometer el secreto.
- Cada key tiene su propio **rate limit configurable** (requests por minuto).
- Se registra `last_used_at` para detectar keys inactivas.

### JWT (para el panel Angular)
- Login con email + contraseña emite un access token (corto) y refresh token.
- Roles: `SuperAdmin` y `Client`.
- El panel del cliente solo consume endpoints del namespace `/me/*` y `/admin/…` está reservado al super-admin.

### Autorización
- Todo endpoint bajo `/files/*` y `/folders/*` requiere API Key válida y activa.
- Todo endpoint bajo `/me/*` requiere JWT de rol `Client`.
- Todo endpoint bajo `/admin/*` requiere JWT de rol `SuperAdmin`.
- Cada request se resuelve al `ClientId` propietario para aislar datos.

---

## 5. Modelo de dominio

Entidades principales (nombres tentativos; los tipos son orientativos):

**Client**
`id`, `email`, `passwordHash`, `name`, `quotaBytes`, `usedBytes`, `isActive`, `trashRetentionDays`, `maxFileSizeBytes` (override opcional), `createdAt`, `updatedAt`

**ApiKey**
`id`, `clientId`, `name`, `keyHash`, `prefix`, `rateLimitPerMinute`, `isActive`, `lastUsedAt`, `createdAt`, `revokedAt`

**Folder**
`id`, `clientId`, `parentFolderId` (nullable = raíz), `name`, `path` (cache calculado tipo `/docs/2025/facturas`), `createdAt`

**File**
`id`, `clientId`, `folderId`, `originalName`, `currentVersionId`, `sizeBytes`, `mimeType`, `extension`, `isDeleted`, `deletedAt`, `createdAt`, `updatedAt`

**FileVersion**
`id`, `fileId`, `versionNumber`, `storagePath`, `sizeBytes`, `mimeType`, `checksumSha256`, `uploadedByApiKeyId`, `createdAt`

**AuditLog**
`id`, `clientId`, `actorType` (Client/ApiKey/SuperAdmin), `actorId`, `action` (Upload/Download/Delete/Move/Rename/Restore/CreateFolder/RotateKey/…), `resourceType`, `resourceId`, `metadataJson`, `ipAddress`, `userAgent`, `createdAt`

**AllowedFileType**
`id`, `extension`, `mimeType`, `isEnabled`, `updatedByAdminId`, `updatedAt`

**AppConfig** (clave/valor tipado para configuración global)
`key`, `value`, `updatedAt`

Índices críticos: `File(clientId, folderId, isDeleted)`, `File(clientId, deletedAt)` para la papelera, `AuditLog(clientId, createdAt)`, `ApiKey(prefix)` único.

---

## 6. Reglas de negocio clave

### Subida de archivo
1. Validar API Key activa y su rate limit.
2. Validar cliente activo.
3. Validar tamaño ≤ `maxFileSizeBytes` (default global u override del cliente).
4. Validar extensión y MIME en la whitelist activa.
5. Validar `usedBytes + fileSize ≤ quotaBytes`.
6. Escribir binario a disco en la ruta física.
7. Registrar `File` + `FileVersion` en BD (transacción).
8. Actualizar `usedBytes` del cliente.
9. Escribir entrada en `AuditLog`.

Si el archivo ya existe en la carpeta con el mismo nombre, se crea una **nueva versión** del archivo existente (no un archivo distinto).

### Descarga
- Endpoint autenticado `GET /files/{id}`.
- Se valida propiedad (el archivo pertenece al cliente de la API Key).
- Se puede pedir versión específica: `GET /files/{id}?version=N`.
- Streaming del binario con `Content-Disposition: attachment`.
- Se registra en `AuditLog`.

### Borrado
- Por defecto es **soft delete**: marca `isDeleted=true`, `deletedAt=NOW()`.
- El archivo va a la papelera y **sigue ocupando cuota** hasta que se purgue (esto fuerza al cliente a vaciar papelera o esperar la auto-purga; evita acumulación silenciosa).
- Un job en background purga archivos con `deletedAt < NOW() - trashRetentionDays` y libera cuota.
- El cliente puede restaurar desde la papelera o forzar hard delete.

### Versionado
- Cada subida al mismo `(folderId, originalName)` incrementa `versionNumber`.
- Todas las versiones cuentan para la cuota.
- El cliente puede listar versiones, descargar una específica o **restaurar** una versión antigua como la actual.

### Rate limiting
- Contador por API Key con ventana de 1 minuto (sugerido Redis, o memoria in-process si el deployment es de un solo nodo).
- Al exceder devuelve `429 Too Many Requests` con header `Retry-After`.

### Cuota
- Al aproximarse al 80% y al 95% se puede disparar una notificación (ver "Decisiones pendientes").
- Si se rechaza una subida por cuota, se responde `413 Payload Too Large` con detalle.

---

## 7. API REST — endpoints principales

### Autenticación (panel)
```
POST   /auth/login              email + password → JWT + refresh
POST   /auth/refresh
POST   /auth/logout
```

### Super-admin (JWT rol SuperAdmin)
```
GET    /admin/clients                 listar
POST   /admin/clients                 crear cliente
GET    /admin/clients/{id}
PATCH  /admin/clients/{id}            ajustar cuota, bloquear, cambiar retención
DELETE /admin/clients/{id}            soft delete de cuenta
GET    /admin/stats                   métricas globales
GET    /admin/config                  ver config global
PATCH  /admin/config                  actualizar (max file size, tipos permitidos, purge days default)
GET    /admin/allowed-types
PATCH  /admin/allowed-types           gestionar whitelist
```

### Panel cliente (JWT rol Client, namespace /me)
```
GET    /me                            perfil
GET    /me/usage                      { usedBytes, quotaBytes, filesCount, trashBytes }
GET    /me/stats                      series temporales para gráficas
GET    /me/api-keys
POST   /me/api-keys                   { name } → key completa (única vez)
PATCH  /me/api-keys/{id}              renombrar, cambiar rate limit
POST   /me/api-keys/{id}/revoke
GET    /me/audit-log                  con filtros
```

### API pública de archivos (API Key)
```
POST   /files                         multipart; querystring: folderId
GET    /files                         filtros: folderId, name, deleted
GET    /files/{id}                    descarga (opcional ?version=N)
GET    /files/{id}/metadata
PATCH  /files/{id}                    renombrar / mover a otra carpeta
DELETE /files/{id}                    soft delete → papelera

GET    /files/{id}/versions
POST   /files/{id}/versions/{n}/restore

GET    /trash
POST   /trash/{id}/restore
DELETE /trash/{id}                    hard delete inmediato

GET    /folders
POST   /folders                       { name, parentId }
PATCH  /folders/{id}                  renombrar / mover
DELETE /folders/{id}                  requiere estar vacía o `?recursive=true`
```

Respuestas de error uniformes tipo Problem Details (RFC 7807).

---

## 8. Panel Angular — vistas principales

**Vistas del Cliente**
- **Dashboard**: consumo actual (barra `usedBytes / quotaBytes`), número de archivos, gráfica de uploads/downloads en el tiempo, últimas 10 acciones del audit log.
- **Explorador de archivos**: navegación tipo Dropbox por carpetas, listado con nombre/tamaño/fecha, acciones por archivo (descargar, mover, renombrar, ver versiones, borrar), papelera aparte.
- **Papelera**: archivos eliminados con fecha de auto-purga, botones restaurar / eliminar definitivamente.
- **API Keys**: listado con nombre, prefijo, último uso, rate limit; acciones crear, rotar (revoca + genera nueva), revocar, editar.
- **Auditoría**: tabla filtrable de eventos (fecha, acción, recurso, actor, IP).
- **Perfil**: cambiar contraseña.

**Vistas del Super-admin**
- **Dashboard global**: número de clientes activos, uso total del disco, top clientes por consumo, subidas totales por día.
- **Clientes**: listado con búsqueda, creación (email + cuota inicial), detalle con edición de cuota y bloqueo.
- **Configuración global**: tamaño máximo default, días de retención de papelera default, whitelist de tipos permitidos.
- **Auditoría global**: opcional, ver eventos cross-cliente.

---

## 9. Almacenamiento físico

Estructura sugerida en disco:
```
/storage/
  {clientId}/
    {yyyy}/{mm}/
      {fileVersionId}.bin
```

- Nunca usar el `originalName` en la ruta física (evita colisiones, path traversal, caracteres inválidos).
- El `originalName` vive solo en BD.
- Fragmentar por año/mes evita directorios con millones de archivos.
- El `fileVersionId` es un UUID y se guarda en `FileVersion.storagePath`.
- La ruta base `/storage` es configurable por variable de entorno.

Consideraciones:
- Backups del directorio `/storage` + dump de PostgreSQL en el mismo momento (consistencia).
- Permisos de filesystem: el proceso .NET debe ser el único con escritura sobre `/storage`.

---

## 10. Observabilidad y auditoría

- **Audit log** en BD con toda operación de escritura y descarga.
- **Structured logging** con Serilog (JSON en producción, consola en dev).
- **Health checks** de .NET expuestos en `/health` y `/health/ready` (BD + disco).
- **Métricas** vía OpenTelemetry o `System.Diagnostics.Metrics` (opcional, según despliegue).
- El audit log del cliente se muestra en su panel; el del super-admin ve todos (opcional).

---

## 11. Configuración por deployment

Todo lo siguiente debe ser configurable sin recompilar:

| Configuración | Ámbito | Default sugerido |
|---|---|---|
| `MaxFileSizeBytes` global | Global | 10 MB |
| `MaxFileSizeBytes` por cliente | Override | null (usa global) |
| `TrashRetentionDays` global | Global | 30 |
| `TrashRetentionDays` por cliente | Override | null |
| `AllowedFileTypes` | Global | jpg, png, gif, webp, pdf, docx, xlsx, txt, csv |
| `StoragePath` | Env var | `/var/lib/filestore` |
| `JwtSecret`, `JwtLifetime` | Env var | — |
| `RateLimitDefault` (per API Key) | Global | 100/min |
| Cadena de conexión PostgreSQL | Env var | — |

---

## 12. Stack técnico y decisiones

**Backend — .NET (última LTS)**
- Clean Architecture + CQRS con MediatR (dado el volumen de casos de uso y la necesidad de mantenibilidad a largo plazo).
- EF Core + Npgsql para PostgreSQL.
- FluentValidation para validación de comandos.
- ASP.NET Identity o implementación custom para usuarios/JWT.
- Swashbuckle para OpenAPI/Swagger.
- Serilog para logging estructurado.
- Background service (`IHostedService`) para la purga de papelera.

**Frontend — Angular (última versión estable)**
- Tailwind CSS.
- Componentes reutilizables (design system propio).
- Interceptores para JWT y refresh automático.
- Guards para rutas protegidas por rol.
- i18n opcional con ngx-translate si se contempla multi-idioma.

**Base de datos — PostgreSQL 16**
- Migraciones vía EF Core.
- Índices en columnas de filtrado frecuente.

**Infra de desarrollo**
- Docker Compose con PostgreSQL, API .NET, Angular dev server.
- Volumen montado para `/storage`.

**¿Qué falta al stack?** Nada obligatorio. Opcionales según crecimiento:
- **Redis** si el rate limiting o los stats vivos escalan (para un solo nodo, memoria basta).
- **Servicio de correo** (SMTP o SendGrid) si se envían notificaciones.
- **ClamAV** si se decide escaneo antivirus.

---

## 13. Fuera de alcance / roadmap futuro

Cosas que **no** entran en el MVP pero conviene tener listadas:
- URLs firmadas temporales para compartir archivos públicamente.
- Webhooks para notificar eventos al cliente (`file.uploaded`, `quota.warning`).
- Deduplicación por checksum (dos archivos idénticos → un solo binario).
- Encriptación en reposo a nivel aplicación.
- Antivirus scan (ClamAV) en uploads.
- Multi-región / múltiples nodos de almacenamiento.
- Planes/tiers de suscripción y facturación.
- SDK cliente (.NET/JS/Python) para integraciones.

---

## 14. Estructura de documentación propuesta

Con esta base, sugiero generar los siguientes documentos técnicos (cada uno en su propio archivo):

1. **`01-Vision-y-Alcance.md`** — versión más pulida de este documento para stakeholders.
2. **`02-Arquitectura.md`** — diagrama de capas, componentes, flujos, decisiones arquitectónicas (ADRs).
3. **`03-Modelo-de-Datos.md`** — diagrama ER, DDL completo de PostgreSQL, índices, migraciones.
4. **`04-API-Reference.md`** — especificación detallada de cada endpoint (paths, request/response, códigos de error) y/o OpenAPI YAML.
5. **`05-Autenticacion-y-Seguridad.md`** — flujo de API Keys, JWT, rotación, hashing, validaciones anti-abuso.
6. **`06-Almacenamiento-Fisico.md`** — estructura en disco, backups, permisos.
7. **`07-Panel-Angular.md`** — mapa de rutas, vistas, componentes, mockups/wireframes.
8. **`08-Configuracion-y-Deployment.md`** — variables de entorno, Docker Compose, checklist de puesta en producción.
9. **`09-Observabilidad-y-Auditoria.md`** — logs, métricas, audit log, health checks.
10. **`10-Roadmap.md`** — features fuera de MVP con prioridad y esfuerzo estimado.

---

## 15. Decisiones pendientes / puntos para confirmar

Antes de arrancar la documentación detallada, hay algunas decisiones menores que conviene cerrar:

1. **¿La papelera cuenta en la cuota?** Propuesta: **sí** (más seguro, evita acumulación silenciosa). Si prefieres que no cuente, el cálculo de `usedBytes` cambia.
2. **¿Notificaciones al cliente por email?** (al 80%/95% de cuota, al bloquear cuenta, al crear API key). Requiere agregar SMTP o similar.
3. **¿Backups automáticos?** Estrategia sugerida: snapshot diario de BD + rsync de `/storage`. Fuera del MVP, pero conviene definirlo.
4. **¿Multi-idioma en el panel?** Si es solo español, se ahorra ngx-translate.
5. **¿Deployment target?** ¿Servidor Linux propio, VPS, VM cloud? Impacta el capítulo de deployment.
6. **¿Antivirus scan?** Recomendación: fuera del MVP salvo requisito regulatorio.

---

**¿Cómo procedemos?** Puedo empezar a generar los documentos numerados de la sección 14 en el orden que prefieras. Sugiero arrancar por **Arquitectura (02)** y **Modelo de Datos (03)** en paralelo, porque son la base sobre la que se construye todo lo demás.
