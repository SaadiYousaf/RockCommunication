#!/usr/bin/env bash
# Runs ONCE when the codespace is first created.
# Generates .env (with a fresh JWT secret) if it doesn't exist.

set -euo pipefail
cd "$(dirname "$0")/.."

if [ ! -f .env ]; then
  echo "🔐 Generating .env with a random JWT secret…"
  JWT=$(openssl rand -base64 48 | tr -d '\n')
  cat > .env <<EOF
APP_URL=http://localhost:8080
WEB_PORT=8080

JWT_SECRET=$JWT
JWT_ISSUER=CRM
JWT_AUDIENCE=CRM
JWT_ACCESS_MINUTES=15
JWT_REFRESH_DAYS=7

DATABASE_PROVIDER=Sqlite
CONNECTION_STRING=Data Source=/data/crm.db

EMAIL_PROVIDER=Smtp
SMTP_HOST=smtp-relay.brevo.com
SMTP_PORT=465
SMTP_USERNAME=
SMTP_PASSWORD=
EMAIL_FROM=no-reply@crm.local
EMAIL_FROM_NAME=Rock Communication CRM
SUPPORT_EMAIL=support@crm.local

SMS_PROVIDER=Stub
SMS_API_KEY=
SMS_FROM=

AI_PROVIDER=Stub
AI_API_KEY=
AI_MODEL=gpt-4o-mini
EOF
  chmod 600 .env
fi

echo "✅ post-create done"
