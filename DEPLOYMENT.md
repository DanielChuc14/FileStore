# Despliegue en un VPS Linux

Guia para desplegar FileStore en un servidor propio con Docker Compose, Nginx y
TLS de Let's Encrypt. El orden de los pasos importa: cada seccion asume la
anterior hecha.

---

## 1. Requisitos

- Un VPS con Linux (Debian/Ubuntu de referencia) y acceso root.
- Un dominio apuntando al servidor (registro A), por ejemplo
  `filestore.tudominio.com`.
- Docker y el plugin Compose instalados.
- Puertos 80 y 443 abiertos en el firewall.

---

## 2. Cifrado del disco en reposo (antes de instalar nada)

El cifrado a nivel de disco protege los archivos y la base si el disco es robado
o la VM se desmantela. Se hace con LUKS sobre la particion o disco de datos,
**antes** de poner datos ahi.

```bash
# Sobre el disco/particion de datos (ejemplo /dev/sdb). BORRA su contenido.
cryptsetup luksFormat /dev/sdb
cryptsetup open /dev/sdb datos
mkfs.ext4 /dev/mapper/datos
mkdir -p /mnt/datos
mount /dev/mapper/datos /mnt/datos
```

Docker se configura para guardar sus datos ahi (los volumenes de Postgres y
`/storage` viven bajo `/var/lib/docker/volumes`):

```bash
# Mover el data-root de Docker al disco cifrado.
systemctl stop docker
mv /var/lib/docker /mnt/datos/docker
# En /etc/docker/daemon.json:  { "data-root": "/mnt/datos/docker" }
systemctl start docker
```

**Importante:** LUKS pide la passphrase al montar. En un reinuedo desatendido el
disco no se monta solo salvo que configures una clave por archivo o TPM. Decidir
segun cuanta intervencion manual toleres en un reboot.

**El cifrado de disco NO cubre los backups**: ver seccion 8.

---

## 3. Certificado TLS (antes de levantar el stack)

Nginx no arranca si el certificado no existe todavia (su config lo referencia).
Por eso se emite primero, con certbot en modo standalone (ocupa el puerto 80 un
momento; asegurate de que nada mas lo use):

```bash
apt install certbot
certbot certonly --standalone -d filestore.tudominio.com
```

Deja los certificados en `/etc/letsencrypt/live/filestore.tudominio.com/`. Esa
ruta va en `CERTS_PATH` del `.env`.

---

## 4. Rol de base de datos

La imagen oficial de Postgres crea el `POSTGRES_USER` como **superusuario**. Aca
es aceptable porque el contenedor de Postgres es dedicado a FileStore, no expone
su puerto al host y no comparte la instancia con otras bases: el radio de daño se
limita a los datos de la propia app.

Si querés endurecerlo, se puede crear un rol de aplicacion sin superusuario con
un script en `/docker-entrypoint-initdb.d/` y apuntar la cadena de conexion a
ese rol, dejando el superusuario solo para administracion. No es obligatorio
para el MVP.

---

## 5. Configuracion (.env)

```bash
cp .env.production.example .env
```

Completar cada valor. Generar los secretos con:

```bash
openssl rand -base64 48   # JWT_SECRET
openssl rand -base64 24   # POSTGRES_PASSWORD
openssl rand -base64 18   # SUPERADMIN_PASSWORD
```

El `.env` **no se commitea** (ya esta en `.gitignore`). Guardá la contraseña del
super-admin en un gestor: se usa para el primer login y no se puede recuperar.

---

## 6. Primer despliegue

En el primer arranque hay que crear el esquema. Se activa la migracion al
arrancar poniendo en el `.env`:

```
APPLY_MIGRATIONS=true
```

Levantar el stack:

```bash
docker compose -f docker-compose.prod.yml --env-file .env up -d --build
```

Verificar:

```bash
docker compose -f docker-compose.prod.yml ps          # los 3 servicios up
docker compose -f docker-compose.prod.yml logs api     # "Super-admin creado"
curl -k https://filestore.tudominio.com/api/health     # Healthy
```

Cuando el esquema ya exista, **volver a `APPLY_MIGRATIONS=false`** y re-desplegar:
asi un deploy futuro no altera el esquema sin que lo decidas. Para migraciones
posteriores, aplicarlas como paso explicito y consciente.

---

## 7. Actualizaciones

```bash
git pull
docker compose -f docker-compose.prod.yml --env-file .env up -d --build
```

Si una actualizacion trae una migracion nueva, aplicarla de forma explicita
(activar `APPLY_MIGRATIONS=true` para ese arranque y volver a false, o correr la
migracion aparte).

---

## 8. Backups

La base y los binarios deben respaldarse **en el mismo momento** para que queden
consistentes: un dump de la base sin los archivos que referencia, o al reves, es
un backup roto.

```bash
#!/bin/bash
# backup.sh — correr por cron, ej. diario.
set -e
STAMP=$(date +%Y%m%d-%H%M%S)
DEST=/mnt/backups

# 1. Dump de la base.
docker compose -f docker-compose.prod.yml exec -T postgres \
  pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB" | gzip > "$DEST/db-$STAMP.sql.gz"

# 2. Archivos del volumen storage.
docker run --rm -v filestore_storage-data:/storage -v "$DEST":/backup alpine \
  tar czf "/backup/storage-$STAMP.tar.gz" -C /storage .

# 3. Cifrar en destino: el cifrado de disco NO protege un backup que se copia
#    a otro lado. Se cifra con una clave que NO viva en el mismo servidor.
gpg --encrypt --recipient tu@email.com "$DEST/db-$STAMP.sql.gz"
gpg --encrypt --recipient tu@email.com "$DEST/storage-$STAMP.tar.gz"
rm "$DEST/db-$STAMP.sql.gz" "$DEST/storage-$STAMP.tar.gz"
```

Probar la restauracion de vez en cuando: un backup que nunca se restauro no es
un backup, es una suposicion.

---

## 9. Renovacion del certificado

Let's Encrypt vence a los 90 dias. La renovacion usa el webroot que ya monta el
compose, sin apagar Nginx:

```bash
# En cron, ej. semanal.
certbot renew --webroot -w /var/lib/docker/volumes/filestore_certbot-webroot/_data
docker compose -f docker-compose.prod.yml exec frontend nginx -s reload
```

---

## 10. Checklist final

- [ ] Disco de datos cifrado con LUKS.
- [ ] Certificado TLS emitido y renovacion en cron.
- [ ] `.env` completo, con secretos generados al azar, fuera del repo.
- [ ] Puertos 80 y 443 abiertos; Postgres y la API **no** expuestos al host.
- [ ] Primer deploy hecho, esquema creado, `APPLY_MIGRATIONS` de vuelta en false.
- [ ] Login del super-admin verificado.
- [ ] Backup automatico configurado, cifrado, y una restauracion probada.
- [ ] Contraseña del super-admin guardada en un gestor.
