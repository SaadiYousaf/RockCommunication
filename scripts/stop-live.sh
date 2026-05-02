#!/bin/bash
# Stops backend + frontend + tunnel started by go-live.sh
echo "▶ Stopping CRM services..."
lsof -ti :5293 -sTCP:LISTEN 2>/dev/null | xargs kill -9 2>/dev/null && echo "  backend stopped"  || echo "  backend already stopped"
lsof -ti :5173 -sTCP:LISTEN 2>/dev/null | xargs kill -9 2>/dev/null && echo "  frontend stopped" || echo "  frontend already stopped"
pkill -f "cloudflared tunnel run crm" 2>/dev/null && echo "  tunnel stopped" || echo "  tunnel already stopped"
echo "✓ Done."
