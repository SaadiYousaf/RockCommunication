#!/usr/bin/env bash
#
# One-command production deploy — a self-contained fallback for when the GitHub
# Actions pipeline (.github/workflows/deploy.yml) is unavailable (e.g. the
# account's Actions minutes are exhausted and jobs sit "queued" forever).
#
# It mirrors CI exactly: build backend + frontend locally, rsync the artifacts
# to the Lightsail box (preserving its prod config + SQLite DB), restart the
# services, then verify the new build is live at the edge.
#
# Builds run on THIS machine (the box only has ~1 GB RAM — not enough to build
# the frontend), so no GitHub-hosted runner is involved.
#
# Usage:
#   ./scripts/deploy.sh
#
# Overridable via env:
#   SSH_KEY   path to the Lightsail private key   (default: ~/.ssh/lightsail.pem)
#   SSH_HOST  ubuntu@<box-ip>                      (default: ubuntu@13.207.198.3)
#   API_URL   prod API origin baked into the SPA   (default: https://api.smhachieverslifegroup.com)
#   APP_URL   prod app origin (edge verify)        (default: https://app.smhachieverslifegroup.com)
#
set -euo pipefail

SSH_KEY="${SSH_KEY:-$HOME/.ssh/lightsail.pem}"
# NOTE: this Lightsail instance has a DYNAMIC public IP, so it CHANGES on every stop/start
# (13.127.227.148 -> 13.207.198.3 after the Aug-2026 outage). Attach a static IP in
# Lightsail -> Networking to stop this recurring, and keep the SSH_HOST GitHub secret in sync.
SSH_HOST="${SSH_HOST:-ubuntu@13.207.198.3}"
API_URL="${API_URL:-https://api.smhachieverslifegroup.com}"
APP_URL="${APP_URL:-https://app.smhachieverslifegroup.com}"
BOX_REPO="/home/ubuntu/CRM"

cd "$(dirname "$0")/.."
ROOT="$(pwd)"
SHA="$(git rev-parse HEAD)"
SHORT="${SHA:0:12}"
SSH_OPTS=(-i "$SSH_KEY" -o StrictHostKeyChecking=accept-new)

echo "▸ Deploying ${SHA:0:7} to $SSH_HOST"

echo "▸ [1/5] Publishing backend…"
dotnet publish backend/src/CRM.Api/CRM.Api.csproj -c Release -o "$ROOT/api-publish" >/dev/null

echo "▸ [2/5] Building frontend (VITE_BUILD_ID=$SHORT)…"
( cd frontend && VITE_API_URL="$API_URL" VITE_BUILD_ID="$SHA" npm run build >/dev/null )

echo "▸ [3/5] Syncing artifacts to the box…"
# App_Data holds user UPLOADS (Pulse images, employee documents, avatars). It lives under
# the app dir but is NOT in the publish output, so without excluding it the --delete below
# would erase every uploaded file on each deploy (the SQLite DB survives only because it's at
# /data/crm.db, outside this dir). Exclude ⇒ uploads are preserved across deploys.
rsync -az --delete --exclude 'appsettings.Production.json' --exclude 'App_Data' \
  -e "ssh ${SSH_OPTS[*]}" "$ROOT/api-publish/" "$SSH_HOST:$BOX_REPO/api-publish/"
# Frontend: NO --delete. index.html (stable name) is overwritten with the new
# build's chunk references, but the previous build's hashed assets/*.js are KEPT.
# That way a browser tab still holding the OLD index.html can keep lazy-loading
# its old chunks after a deploy instead of 404-ing ("This page ran into a problem").
# Assets accumulate slowly (~1-2 MB/build); prune old ones occasionally if needed.
rsync -az \
  -e "ssh ${SSH_OPTS[*]}" "$ROOT/frontend/dist/" "$SSH_HOST:$BOX_REPO/frontend/dist/"

echo "▸ [4/5] Restarting services…"
ssh "${SSH_OPTS[@]}" "$SSH_HOST" '
  sudo systemctl restart crm-api
  sudo systemctl reload nginx || sudo systemctl restart nginx
  for i in $(seq 1 30); do
    [ "$(curl -s -o /dev/null -w "%{http_code}" http://127.0.0.1:5293/health)" = "200" ] && { echo "  backend healthy after ${i}s"; exit 0; }
    sleep 1
  done
  echo "  backend did NOT become healthy" >&2; exit 1
'

echo "▸ [5/5] Verifying at the edge…"
for i in $(seq 1 10); do
  live="$(curl -s --max-time 10 "$APP_URL/index.html?cb=$(date +%s)-$i" | grep -oE 'index-[^"]+\.js' | head -1 || true)"
  if printf '%s' "$live" | grep -q "\.$SHORT"; then
    echo "  ✓ live: $live"
    echo "  ✓ health: $(curl -s -o /dev/null -w '%{http_code}' --max-time 10 "$API_URL/health")"
    echo "✅ Deploy complete: ${SHA:0:7}"
    exit 0
  fi
  sleep 6
done
echo "⚠️  New build not visible at the edge yet (Cloudflare cache?) — check $APP_URL" >&2
exit 1
