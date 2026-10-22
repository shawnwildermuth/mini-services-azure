#!/bin/sh
# Writes /usr/share/nginx/html/env.js from the container's environment, so a single image can be pointed at any API
# and any Entra ID tenant. Runs (as the nginx user) from the base image's /docker-entrypoint.d/ before nginx starts.
set -eu

out=/usr/share/nginx/html/env.js

# Values end up inside a JavaScript string, so accept only characters that appear in URLs and ids; anything else
# (quotes, backslashes, angle brackets, whitespace) is refused rather than escaped.
safe() {
  case "$1" in
    *[!A-Za-z0-9:/._@?=\&%+~#,-]*) echo "10-env-js.sh: $2 contains characters that are not allowed" >&2; exit 1 ;;
  esac
  printf '%s' "$1"
}

: "${API_BASE_URL:?API_BASE_URL must be set, e.g. https://my-api.azurewebsites.net}"
AUTH_MODE="${AUTH_MODE:-msal}"

case "$AUTH_MODE" in
  msal)
    : "${TENANT_ID:?TENANT_ID must be set when AUTH_MODE=msal}"
    : "${CLIENT_ID:?CLIENT_ID must be set when AUTH_MODE=msal}"
    : "${API_SCOPE:?API_SCOPE must be set when AUTH_MODE=msal, e.g. api://<client-id>/access_as_admin}"
    ;;
  dev) echo "10-env-js.sh: WARNING AUTH_MODE=dev is for local development only" >&2 ;;
  *) echo "10-env-js.sh: AUTH_MODE must be 'msal' or 'dev'" >&2; exit 1 ;;
esac

cat > "$out" <<EOF
// Generated at container start from environment variables.
window.__APP_CONFIG__ = {
  apiBaseUrl: '$(safe "$API_BASE_URL" API_BASE_URL)',
  auth: {
    mode: '$(safe "$AUTH_MODE" AUTH_MODE)',
    tenantId: '$(safe "${TENANT_ID:-}" TENANT_ID)',
    clientId: '$(safe "${CLIENT_ID:-}" CLIENT_ID)',
    apiScope: '$(safe "${API_SCOPE:-}" API_SCOPE)',
  },
}
EOF

echo "10-env-js.sh: wrote $out (auth mode $AUTH_MODE) for API $API_BASE_URL"