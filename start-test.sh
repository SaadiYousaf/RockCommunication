#!/usr/bin/env bash
# One-command launcher: builds + starts the CRM stack and opens a public Dev Tunnel.
#
# Prereqs (one-time, you run these by hand):
#   1. Docker Desktop must be running.
#   2. brew install --cask devtunnel    # or curl -sL https://aka.ms/DevTunnelCliInstall | bash
#   3. devtunnel user login -g          # browser sign-in with GitHub or Microsoft
#   4. devtunnel create crm-test --allow-anonymous
#      devtunnel port create crm-test -p 8080 --protocol https
#
# Then just run:  ./start-test.sh

set -euo pipefail

cd "$(dirname "$0")"

# devtunnel installer drops the binary in ~/bin — make sure bash can see it.
export PATH="$HOME/bin:$HOME/.local/bin:$PATH"

TUNNEL_NAME="crm-test"

# --- 1. sanity checks ----------------------------------------------------------
command -v docker     >/dev/null || { echo "❌ docker not found. Install Docker Desktop."; exit 1; }
command -v devtunnel  >/dev/null || { echo "❌ devtunnel CLI not found. brew install --cask devtunnel"; exit 1; }
docker info >/dev/null 2>&1      || { echo "❌ Docker daemon not running. Open Docker Desktop."; exit 1; }

if ! devtunnel show "$TUNNEL_NAME" >/dev/null 2>&1; then
  echo "❌ Tunnel '$TUNNEL_NAME' doesn't exist yet. Run:"
  echo "   devtunnel user login -g"
  echo "   devtunnel create $TUNNEL_NAME --allow-anonymous"
  echo "   devtunnel port create $TUNNEL_NAME -p 8080 --protocol https"
  exit 1
fi

# --- 2. discover the tunnel's public URL --------------------------------------
# devtunnel uses an internal relay ID (e.g. sdxvchc3), not the friendly name.
# Spin up `devtunnel host` briefly, capture its output, then kill it.
echo "🔎 probing tunnel for its public URL…"
TMP_LOG=$(mktemp)
devtunnel host "$TUNNEL_NAME" > "$TMP_LOG" 2>&1 &
HOST_PID=$!
for _ in $(seq 1 30); do
  if grep -q 'Connect via browser' "$TMP_LOG" 2>/dev/null; then break; fi
  sleep 0.5
done
TUNNEL_URL=$(grep -Eo 'https://[a-z0-9]+-8080\.[a-z0-9]+\.devtunnels\.ms' "$TMP_LOG" | head -1)
kill "$HOST_PID" 2>/dev/null || true
wait "$HOST_PID" 2>/dev/null || true
rm -f "$TMP_LOG"

if [ -z "$TUNNEL_URL" ]; then
  echo "❌ Could not determine tunnel URL. Try: devtunnel host $TUNNEL_NAME"
  exit 1
fi
echo "🔗 Tunnel URL: $TUNNEL_URL"

# --- 3. patch .env so CORS + JWT issuer match the tunnel ----------------------
[ -f .env ] || { echo "❌ .env missing. Re-run setup."; exit 1; }

# macOS sed needs '' for in-place
sed -i '' -E "s|^APP_URL=.*|APP_URL=$TUNNEL_URL|"        .env
sed -i '' -E "s|^JWT_ISSUER=.*|JWT_ISSUER=$TUNNEL_URL|"  .env
sed -i '' -E "s|^JWT_AUDIENCE=.*|JWT_AUDIENCE=$TUNNEL_URL|" .env
echo "✅ .env updated (APP_URL, JWT_ISSUER, JWT_AUDIENCE)"

# --- 4. build + start docker stack --------------------------------------------
echo "🐳 docker compose up -d --build  (first run takes a few minutes)…"
docker compose up -d --build

echo "⏳ waiting for /health to return 200…"
for _ in $(seq 1 60); do
  if curl -fsS http://localhost:8080/health >/dev/null 2>&1; then
    echo "✅ API healthy on http://localhost:8080"
    break
  fi
  sleep 2
done

# --- 5. host the tunnel (foreground — Ctrl+C to stop) -------------------------
echo ""
echo "============================================================"
echo "  Share this URL:  $TUNNEL_URL"
echo "  Login:           superadmin / SuperAdmin@123!"
echo "  Stop:            Ctrl+C (then 'docker compose down' to stop CRM)"
echo "============================================================"
echo ""
exec devtunnel host "$TUNNEL_NAME"
