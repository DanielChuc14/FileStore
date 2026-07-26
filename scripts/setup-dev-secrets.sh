#!/usr/bin/env bash
# Configura de una sola vez los dotnet user-secrets de FileStore.API para
# desarrollo local. Ver SECRETS.md para el detalle de cada secreto.
#
# Uso:
#   ./scripts/setup-dev-secrets.sh          # no toca los secretos que ya existan
#   ./scripts/setup-dev-secrets.sh --force  # los sobreescribe con valores nuevos
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
API_PROJECT="$SCRIPT_DIR/../backend/FileStore.API"
FORCE=false
[[ "${1:-}" == "--force" ]] && FORCE=true

existing_secrets="$(dotnet user-secrets list --project "$API_PROJECT" 2>/dev/null || true)"

has_secret() {
  grep -q "^$1 =" <<<"$existing_secrets"
}

set_secret() {
  local key="$1" value="$2"
  if ! $FORCE && has_secret "$key"; then
    echo "  - $key ya estaba configurado, se deja igual (usa --force para sobreescribir)."
    return
  fi
  dotnet user-secrets set "$key" "$value" --project "$API_PROJECT" >/dev/null
  echo "  - $key configurado."
}

prompt() {
  local label="$1" default="$2" reply
  read -rp "$label [$default]: " reply
  echo "${reply:-$default}"
}

prompt_secret() {
  local label="$1" reply
  read -rsp "$label: " reply
  echo >&2
  echo "$reply"
}

echo "== Conexion a Postgres =="
db_host="$(prompt "Host" "localhost")"
db_port="$(prompt "Puerto" "5432")"
db_name="$(prompt "Base de datos" "filestore")"
db_user="$(prompt "Usuario" "dev_user")"
db_password="$(prompt_secret "Password de $db_user")"
set_secret "ConnectionStrings:Default" \
  "Host=$db_host;Port=$db_port;Database=$db_name;Username=$db_user;Password=$db_password"

echo
echo "== JWT =="
if ! $FORCE && has_secret "Jwt:Secret"; then
  echo "  - Jwt:Secret ya estaba configurado, se deja igual (usa --force para sobreescribir)."
else
  set_secret "Jwt:Secret" "$(openssl rand -base64 48)"
fi

echo
echo "== Super-admin =="
admin_email="$(prompt "Email" "admin@local")"
admin_password="$(prompt_secret "Password para $admin_email (vacio = generar una)")"
if [[ -z "$admin_password" ]]; then
  admin_password="$(openssl rand -base64 18)"
  echo "  Password generada: $admin_password"
fi
admin_name="$(prompt "Nombre" "Super Admin")"
set_secret "SuperAdmin:Email" "$admin_email"
set_secret "SuperAdmin:Password" "$admin_password"
set_secret "SuperAdmin:Name" "$admin_name"

echo
echo "== Correo (Resend) =="
echo "Opcional. Sin esto los correos no se envian: quedan registrados en el log."
resend_key="$(prompt_secret "API key de Resend (vacio = omitir)")"
if [[ -n "$resend_key" ]]; then
  # El remitente es tan obligatorio como la clave: con una sola de las dos, la
  # app considera que el correo no esta configurado y sigue usando el log.
  resend_from="$(prompt "Remitente (dominio verificado en Resend)" "no-reply@localhost")"
  set_secret "Resend:ApiKey" "$resend_key"
  set_secret "Resend:FromAddress" "$resend_from"
else
  echo "  Omitido."
fi

echo
echo "Listo. Secretos actuales:"
dotnet user-secrets list --project "$API_PROJECT"
echo
echo "El super-admin se crea la primera vez que arranques la API con la tabla"
echo "SuperAdmins vacia (dotnet run --project backend/FileStore.API)."
