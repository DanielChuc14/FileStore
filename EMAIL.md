# Correo transaccional (Resend)

Guía de la integración de correo: qué se envía, cómo está montado y qué falta
para activarlo.

---

## Estado: montado, sin enviar todavía

Todo el código está en su sitio y probado. **No sale ni un correo real** hasta
que se configuren `Resend:ApiKey` y `Resend:FromAddress`. Sin ellas, la app
arranca igual y cada envío se registra en el log con un warning
(`LoggingEmailSender`). Eso es deliberado: ni desarrollar ni correr los tests
debería exigir una cuenta de correo.

### Para activarlo

1. Verificar el dominio en Resend (**Domains** → estado `Verified`). Mientras
   diga `Pending` o `Checking DNS`, faltan los registros SPF/DKIM y ningún envío
   va a salir.
2. Configurar las claves. En local, con el script:

   ```bash
   ./scripts/setup-email-secrets.sh
   ```

   Pregunta la clave, el remitente y la URL del panel, y al final ofrece
   **enviar un correo de prueba** para confirmar que todo funciona de punta a
   punta. Si el envío falla, traduce el error de Resend a su causa real (clave
   revocada, dominio sin verificar, clave sin permiso de envío). Es idempotente:
   `--force` sobreescribe, `--test` solo prueba lo ya configurado.

   A mano es equivalente a:

   ```bash
   dotnet user-secrets set "Resend:ApiKey" "re_..." --project backend/FileStore.API
   dotnet user-secrets set "Resend:FromAddress" "no-reply@tudominio.com" --project backend/FileStore.API
   ```

   En producción son `RESEND_API_KEY` y `RESEND_FROM_ADDRESS` en el `.env`.
3. Reiniciar la API. En el log de arranque ya no debería aparecer el warning de
   envío omitido.

**Hacen falta las dos.** Con solo una configurada, `ResendSettings.IsConfigured`
da falso y se sigue usando el log. Es a propósito: un remitente vacío produce
errores en cada envío en vez de un fallo claro al arrancar.

### Sin correo, tres operaciones se niegan a ejecutarse

Dar de alta un cliente genera una contraseña que **solo** viaja por correo: no la
devuelve la API y no se registra en el log. Sin envío real, esa cuenta nacería
inaccesible para siempre, salvo escribiendo un hash a mano en la base. El reseteo
es peor todavía, porque además destruye una contraseña que funcionaba.

Por eso, mientras el correo no esté configurado, estas tres devuelven **`503`**
con un mensaje que dice qué falta, en vez de dejar el destrozo hecho:

- `POST /admin/clients`
- `POST /admin/clients/{id}/reset-password`
- `POST /auth/forgot-password`

La comprobación de `forgot-password` va **antes** de mirar si la cuenta existe: la
respuesta depende solo de la configuración, igual para cualquier email, así que
no abre una vía para enumerar cuentas. Hay un test que lo fija.

Todo lo demás sigue funcionando con normalidad. Crear una API Key, por ejemplo,
manda un aviso por correo, pero ese aviso es informativo: perderlo no rompe nada
y bloquear la operación sería pasarse.

Esto importa en el despliegue: **se puede publicar con el correo apagado, pero no
se pueden crear clientes hasta encenderlo.** El sistema ahora lo dice en vez de
dejarte descubrirlo.

El remitente **tiene que estar en el dominio verificado**. Una dirección de
Gmail o similar no sirve: SPF/DKIM existen precisamente para impedir enviar en
nombre de un dominio ajeno.

---

## Qué se envía

| Cuándo | Plantilla | Contenido |
|---|---|---|
| Alta de cliente | `Welcome` | Usuario y contraseña generada |
| Reseteo por el super-admin | `PasswordReset` | Contraseña nueva |
| Solicitud de recuperación | `PasswordResetLink` | Enlace de un solo uso, vence en 1 h |
| Cuota al 80% / 95% | `QuotaAlert` | Consumo actual y aviso |
| Reuso de refresh token | `SuspiciousSessionActivity` | Sesiones cerradas por seguridad |
| API Key creada o rotada | `ApiKeyActivity` | Nombre y prefijo de la key |

Las plantillas son funciones puras en
`FileStore.Application/Common/Emails/EmailTemplates.cs`, sobre la maquetación
compartida de `EmailLayout.cs`. Todo dato que venga del usuario pasa por
`HtmlEncode`: el nombre del cliente lo elige quien lo da de alta y el correo lo
lee otra persona.

### Por qué el HTML parece anticuado

El HTML de correo no es HTML web. Outlook renderiza con el motor de Word, y casi
todos los clientes ignoran las hojas de estilo externas, flexbox y grid. De ahí
las decisiones que parecen de 2005 y no lo son:

- **Tablas para la estructura**, con `role="presentation"` para que los lectores
  de pantalla no las anuncien como tablas de datos.
- **Estilos en línea**: es lo único que respetan todos los clientes.
- **600 px de ancho**, el estándar que entra sin scroll horizontal.
- **Botones a prueba de balas**: una celda con `bgcolor` y el enlace con padding
  dentro. Outlook ignora el padding de un `<a>` suelto, así que un botón hecho
  solo con CSS le sale como texto plano.
- **Sin imágenes remotas**: la mayoría de los clientes las bloquea, así que un
  logo enlazado se vería como un hueco roto — y una petición externa al abrir el
  correo es un píxel de rastreo de facto. La marca es texto.
- **`color-scheme: light`**: sin esto varios clientes invierten los colores por
  su cuenta en modo oscuro, con resultados impredecibles. Es preferible un correo
  claro y consistente que uno oscuro y roto.
- **Preheader**: el texto de vista previa del buzón. Sin él, el cliente muestra
  las primeras palabras que encuentre, que suelen ser "Hola Fulano".

`EmailTemplatesTests` fija lo que un rediseño podría romper en silencio: que el
nombre vaya escapado, que exista la versión en texto plano y que no haya
recursos remotos.

---

## Cómo está montado

```
Handler  --IEmailQueue.Enqueue()-->  tabla EmailOutbox
                                          |
                          EmailDispatchService (cada 60 s)
                                          |
                              IEmailSender --> API de Resend
```

Tres decisiones que conviene no deshacer:

**Encolar es parte de la transacción.** `IEmailQueue.Enqueue()` no llama a
`SaveChanges`, igual que `IAuditLogger.Record()`. La fila del correo se confirma
junto al cambio que lo motiva, así que un alta que termina en rollback no deja
un correo listo para salir con la contraseña de una cuenta que no existe. Hay un
test que lo fija (`ClientEmailTests.AltaFallida_NoDejaCorreoEncolado`).

**El cuerpo se borra al entregar.** Estos correos llevan contraseñas y enlaces de
un solo uso; conservarlos convertiría `EmailOutbox` en un archivo de credenciales
en claro. Al enviarse, `HtmlBody` y `TextBody` se vacían y queda la fila como
rastro. Los senders tampoco registran el cuerpo en el log.

**El despachador asume una sola instancia de la API.** Es el despliegue previsto
(un VPS con docker compose). Con varias instancias haría falta tomar las filas
con `SELECT ... FOR UPDATE SKIP LOCKED`, o dos despachadores enviarían el mismo
correo dos veces.

### Reintentos

Espera creciente entre intentos (2, 4, 8, 16 min) y se rinde a los 5, marcando
la fila como `Failed` con el motivo en `LastError`.

La lógica vive en `IEmailDispatcher`, no en el `BackgroundService`, por el mismo
motivo que `ITrashPurger` y `IQuotaAlerter`: un hosted service solo se puede
probar esperando a su temporizador. `EmailDispatcherTests` cubre el envío
correcto, el borrado del cuerpo, el reintento programado, la rendición tras N
intentos, que un ya enviado no se reenvíe y que un fallo no corte el lote.

Consultar los atascados:

```sql
SELECT "Recipient", "Subject", "Attempts", "LastError"
FROM "EmailOutbox" WHERE "Status" = 3;
```

---

## Configuración

| Clave | Default | Para qué |
|---|---|---|
| `Resend:ApiKey` | — | Clave de la API. Sin ella no se envía nada |
| `Resend:FromAddress` | — | Remitente, en el dominio verificado |
| `Resend:FromName` | `FileStore` | Nombre visible |
| `Resend:TimeoutSeconds` | `10` | Corte de la llamada a la API |
| `App:PanelUrl` | `http://localhost:4200` | Raíz a la que enlazan los correos |
| `EmailDispatch:Enabled` | `true` | Apagar el despachador |
| `EmailDispatch:IntervalSeconds` | `60` | Frecuencia de entrega |
| `EmailDispatch:BatchSize` | `20` | Correos por ciclo |
| `EmailDispatch:MaxAttempts` | `5` | Intentos antes de rendirse |
| `QuotaAlerts:Enabled` | `true` | Apagar los avisos de cuota |
| `QuotaAlerts:IntervalMinutes` | `60` | Frecuencia de revisión |

En los tests, `Resend:ApiKey` se fija vacío y los dos jobs se apagan de forma
explícita: la suite nunca sale a la red y los avisos de cuota se disparan a mano
con `IQuotaAlerter`.

---

## Recuperación de contraseña

`POST /auth/forgot-password` → correo con enlace → `POST /auth/reset-password`.

Propiedades que sostienen los tests de `PasswordRecoveryTests`:

- **No se puede enumerar cuentas**: siempre responde `204`, exista o no el email.
  La vista del panel muestra el mismo acuse incluso si la petición falla.
- **Un solo uso**: canjear marca `UsedAt`; repetir da `401`.
- **Pedir otro enlace invalida el anterior**, para que no queden varias llaves
  vivas rondando por el correo.
- **Vence en una hora**.
- **Canjear cierra todas las sesiones**, sin excepciones. A diferencia del cambio
  desde el perfil, aquí no hay una sesión actual que valga la pena conservar.
- **Sirve también al super-admin.** Es su única vía de recuperación que no pasa
  por tocar la base a mano. El token identifica a su dueño con `UserId` +
  `UserType`, así que el enlace de una cuenta no puede cambiar la contraseña de
  otra. Lo que protege esa puerta es el conjunto: límite de 10 peticiones por
  minuto y por IP, token de un solo uso que vence en una hora, y la necesidad de
  tener acceso al buzón del administrador.
