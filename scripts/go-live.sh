#!/bin/bash
# Brings the CRM live on https://app.smhachieverslifegroup.com (and api.)
# Runs backend + frontend + cloudflared tunnel in the background.
# Logs land in /tmp/crm-*.log so you can tail them later.
# Stops cleanly with: ./scripts/stop-live.sh
#
# Requirements:
#   - dotnet 10 on PATH
#   - npm installed
#   - cloudflared at /opt/homebrew/bin/cloudflared (brew install cloudflared)
#   - Tunnel "crm" already created (cloudflared tunnel create crm)
#   - ~/.cloudflared/config.yml present

set -euo pipefail

REPO="/Users/saadsaqib/Documents/CRM"
CLOUDFLARED="/opt/homebrew/bin/cloudflared"
PIDS="/tmp/crm-pids"
rm -f "$PIDS"

echo "▶ Stopping any previous instances..."
lsof -ti :5293 -sTCP:LISTEN 2>/dev/null | xargs kill -9 2>/dev/null || true
lsof -ti :5173 -sTCP:LISTEN 2>/dev/null | xargs kill -9 2>/dev/null || true
pkill -f "cloudflared tunnel run crm" 2>/dev/null || true
sleep 1

echo "▶ Building frontend (bakes in https://api.smhachieverslifegroup.com)..."
cd "$REPO/frontend"
npm run build > /tmp/crm-frontend-build.log 2>&1
echo "  built. Log: /tmp/crm-frontend-build.log"

echo "▶ Starting backend on http://localhost:5293..."
cd "$REPO/backend/src/CRM.Api"
ASPNETCORE_ENVIRONMENT=Development nohup dotnet run --no-launch-profile --urls http://localhost:5293 \
  > /tmp/crm-backend.log 2>&1 &
echo $! >> "$PIDS"
echo "  backend pid=$! log=/tmp/crm-backend.log"

echo "▶ Starting frontend preview on http://localhost:5173..."
cd "$REPO/frontend"
nohup npm run preview -- --port 5173 --host 0.0.0.0 \
  > /tmp/crm-frontend.log 2>&1 &
echo $! >> "$PIDS"
echo "  frontend pid=$! log=/tmp/crm-frontend.log"

echo "▶ Starting Cloudflare Tunnel..."
nohup "$CLOUDFLARED" tunnel run crm \
  > /tmp/crm-tunnel.log 2>&1 &
echo $! >> "$PIDS"
echo "  tunnel pid=$! log=/tmp/crm-tunnel.log"

echo ""
echo "▶ Waiting for services to come up..."
sleep 10

echo ""
echo "▶ Health check:"
APP_CODE=$(curl -s -o /dev/null -w "%{http_code}" https://app.smhachieverslifegroup.com/ || echo "fail")
API_CODE=$(curl -s -o /dev/null -w "%{http_code}" https://api.smhachieverslifegroup.com/api/auth/me || echo "fail")
echo "  app.smhachieverslifegroup.com  →  HTTP $APP_CODE   (expect 200)"
echo "  api.smhachieverslifegroup.com  →  HTTP $API_CODE   (expect 401 — auth gate working)"

echo ""
echo "✓ Live."
echo ""
echo "Tail logs:"
echo "  tail -f /tmp/crm-backend.log"
echo "  tail -f /tmp/crm-frontend.log"
echo "  tail -f /tmp/crm-tunnel.log"
echo ""
echo "Keep laptop awake while site is live:"
echo "  caffeinate -d -i -s"
echo ""
echo "Stop everything:"
echo "  $REPO/scripts/stop-live.sh"
