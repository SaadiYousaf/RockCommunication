#!/usr/bin/env bash
# Runs on every codespace start. Patches APP_URL to the codespace's
# forwarded URL and brings the docker stack up.

set -euo pipefail
cd "$(dirname "$0")/.."

# Codespaces injects these env vars at runtime.
if [ -n "${CODESPACE_NAME:-}" ] && [ -n "${GITHUB_CODESPACES_PORT_FORWARDING_DOMAIN:-}" ]; then
  PUBLIC_URL="https://${CODESPACE_NAME}-8080.${GITHUB_CODESPACES_PORT_FORWARDING_DOMAIN}"
  echo "🌐 Public URL will be: $PUBLIC_URL"
  sed -i -E "s|^APP_URL=.*|APP_URL=$PUBLIC_URL|"            .env
  sed -i -E "s|^JWT_ISSUER=.*|JWT_ISSUER=$PUBLIC_URL|"      .env
  sed -i -E "s|^JWT_AUDIENCE=.*|JWT_AUDIENCE=$PUBLIC_URL|"  .env
fi

echo "🐳 docker compose up -d --build  (first run takes a few minutes)…"
docker compose up -d --build

echo ""
echo "============================================================"
echo "  CRM should be live at the forwarded port 8080."
echo "  In the 'Ports' tab below, right-click port 8080 → "
echo "  Port Visibility → Public  (one-time, then it's shareable)."
echo "  Login: superadmin / SuperAdmin@123!"
echo "============================================================"
