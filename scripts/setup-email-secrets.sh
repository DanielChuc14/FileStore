#!/usr/bin/env bash
# Configura los user-secrets del envio de correo (Resend) para desarrollo local,
# y opcionalmente comprueba la configuracion enviando un correo de prueba.
#
# Existe aparte de setup-dev-secrets.sh porque el correo se suele activar mas
# tarde que el resto: cuando el dominio termina de verificarse en Resend. Este
# script toca SOLO las claves de correo, sin volver a preguntar por Postgres ni
# por el super-admin.
#
# Uso:
#   ./scripts/setup-email-secrets.sh           # no toca lo que ya este configurado
#   ./scripts/setup-email-secrets.sh --force   # lo sobreescribe
#   ./scripts/setup-email-secrets.sh --test    # solo prueba lo ya configurado
#
# Ver EMAIL.md para el detalle de la integracion.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
API_PROJECT="$SCRIPT_DIR/../backend/FileStore.API"

FORCE=false
TEST_ONLY=false

for arg in "$@"; do
  case "$arg" in
    --force) FORCE=true ;;
    --test) TEST_ONLY=true ;;
    *) echo "Opcion desconocida: $arg" >&2; exit 2 ;;
  esac
done

existing_secrets="$(dotnet user-secrets list --project "$API_PROJECT" 2>/dev/null || true)"

has_secret() {
  grep -q "^$1 =" <<<"$existing_secrets"
}

read_secret() {
  grep "^$1 = " <<<"$existing_secrets" | sed "s/^$1 = //" || true
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

# Dominios de correo gratuito. No se pueden verificar en Resend porque no son
# tuyos, y es el error mas comun al configurar esto por primera vez: poner la
# direccion personal en vez de una del dominio propio.
is_free_mail_domain() {
  local domain="${1##*@}"
  case "${domain,,}" in
    gmail.com|googlemail.com|hotmail.com|outlook.com|live.com|yahoo.com|yahoo.es|icloud.com|proton.me|protonmail.com|aol.com)
      return 0 ;;
    *) return 1 ;;
  esac
}

# ---------------------------------------------------------------------------
# Configuracion
# ---------------------------------------------------------------------------

if ! $TEST_ONLY; then
  echo "== Correo transaccional (Resend) =="
  echo
  echo "Antes de seguir: el dominio del remitente tiene que aparecer como"
  echo "'Verified' en el panel de Resend (seccion Domains). Mientras diga"
  echo "'Pending' o 'Checking DNS', ningun envio va a salir."
  echo

  api_key="$(prompt_secret "API key de Resend (vacio = omitir)")"

  if [[ -z "$api_key" ]]; then
    echo "  Omitido. Sin clave, la app registra los correos en el log en vez de enviarlos."
    exit 0
  fi

  # Aviso, no bloqueo: el prefijo podria cambiar en el futuro y no vale la pena
  # impedir que alguien configure una clave valida por un formato inesperado.
  if [[ "$api_key" != re_* ]]; then
    echo "  Aviso: las claves de Resend suelen empezar por 're_'. Revisa que sea la correcta."
  fi

  from_address="$(prompt "Remitente (en tu dominio verificado)" "no-reply@example.com")"

  if [[ "$from_address" != *@*.* ]]; then
    echo "Error: '$from_address' no parece una direccion de correo." >&2
    exit 1
  fi

  if is_free_mail_domain "$from_address"; then
    echo >&2
    echo "Error: '${from_address##*@}' es un dominio de correo gratuito y no se puede" >&2
    echo "verificar en Resend, porque no es tuyo. SPF/DKIM existen justamente para" >&2
    echo "impedir enviar en nombre de un dominio ajeno; todos los envios darian error." >&2
    echo >&2
    echo "Usa una direccion de tu propio dominio, del tipo no-reply@tudominio.com." >&2
    exit 1
  fi

  from_name="$(prompt "Nombre visible del remitente" "FileStore")"
  panel_url="$(prompt "URL del panel (a la que enlazan los correos)" "http://localhost:4200")"

  echo
  set_secret "Resend:ApiKey" "$api_key"
  set_secret "Resend:FromAddress" "$from_address"
  set_secret "Resend:FromName" "$from_name"
  set_secret "App:PanelUrl" "$panel_url"

  # Se releen: si algo ya estaba y no se uso --force, los valores efectivos son
  # los viejos, no los que se acaban de escribir.
  existing_secrets="$(dotnet user-secrets list --project "$API_PROJECT" 2>/dev/null || true)"
fi

# ---------------------------------------------------------------------------
# Comprobacion
# ---------------------------------------------------------------------------

effective_key="$(read_secret "Resend:ApiKey")"
effective_from="$(read_secret "Resend:FromAddress")"

echo
if [[ -z "$effective_key" || -z "$effective_from" ]]; then
  echo "Faltan claves: hacen falta Resend:ApiKey Y Resend:FromAddress."
  echo "Con solo una, la app considera que el correo no esta listo y sigue usando el log."
  exit 1
fi

echo "Configuracion efectiva:"
echo "  Remitente: $effective_from"
echo "  Panel:     $(read_secret "App:PanelUrl")"
echo

if ! command -v curl >/dev/null 2>&1; then
  echo "curl no esta disponible: se omite el envio de prueba."
  exit 0
fi

test_to="$(prompt "Enviar un correo de prueba a (vacio = omitir)" "")"

if [[ -z "$test_to" ]]; then
  echo "Listo. Reinicia la API para que tome los secretos nuevos."
  exit 0
fi

echo
echo "Enviando..."

response="$(curl -sS -w $'\n%{http_code}' -X POST https://api.resend.com/emails \
  -H "Authorization: Bearer $effective_key" \
  -H "Content-Type: application/json" \
  -d "{\"from\":\"$effective_from\",\"to\":[\"$test_to\"],\"subject\":\"Prueba de FileStore\",\"text\":\"Si recibes esto, el envio de correo de FileStore quedo configurado correctamente.\"}" \
  || true)"

status="$(tail -n1 <<<"$response")"
body="$(sed '$d' <<<"$response")"

if [[ "$status" == "200" ]]; then
  echo "OK: Resend acepto el envio. Revisa la bandeja de $test_to."
  echo "Reinicia la API para que tome los secretos nuevos."
  exit 0
fi

echo "FALLO (HTTP $status):" >&2
echo "$body" >&2
echo >&2

# Los tres errores que se ven en la practica, con su causa real.
case "$body" in
  *"API key is invalid"*)
    echo "La clave no es valida. Si la rotaste en el panel de Resend, la anterior" >&2
    echo "queda revocada al instante: vuelve a correr este script con --force." >&2
    ;;
  *"domain is not verified"*|*"not verified"*)
    echo "El dominio de '$effective_from' aun no esta verificado en Resend." >&2
    echo "Revisa la seccion Domains: tiene que decir 'Verified', no 'Pending'." >&2
    echo "La propagacion de DNS puede tardar horas." >&2
    ;;
  *"restricted"*)
    echo "La clave existe pero no tiene permiso de envio. Crea una con acceso" >&2
    echo "'Sending access' en el panel de Resend." >&2
    ;;
esac

exit 1
