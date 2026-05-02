# Deployment

The whole stack ships as two containers: **api** (.NET 10 / ASP.NET) and **web** (nginx serving the built React bundle and reverse-proxying `/api` and `/hubs` to the api).

## Local / single-host

```bash
cp .env.example .env
# edit .env — at minimum set JWT_SECRET, SMTP creds, EMAIL_FROM, APP_URL
docker compose up -d --build
```

Open http://localhost:8080. The default admin login is seeded on first start (`admin@crm.local` / see seed code).

To rebuild after pulling changes:

```bash
docker compose build && docker compose up -d
```

To wipe state (⚠ destroys the database):

```bash
docker compose down -v
```

## Required env vars

| Variable        | Why                                                      |
|-----------------|----------------------------------------------------------|
| `JWT_SECRET`    | Token signing — generate with `openssl rand -base64 48`. |
| `APP_URL`       | Used for CORS allow-list and email confirm/reset links.  |
| `SMTP_*`        | Password reset and email confirmation won't work without.|
| `EMAIL_FROM`    | Must be a verified sender at your SMTP relay (Brevo/SES).|

## Switching to SQL Server

In `.env`:

```
DATABASE_PROVIDER=SqlServer
CONNECTION_STRING=Server=sqlserver;Database=crm;User Id=sa;Password=<strong>;TrustServerCertificate=True;
```

…and add a `sqlserver` service to `docker-compose.yml`. The provider switch is honoured at startup ([DependencyInjection.cs:47](backend/src/CRM.Infrastructure/DependencyInjection.cs#L47)). EF migrations run on first boot.

## Behind a reverse proxy / TLS

Terminate TLS at your edge (Caddy, Traefik, Cloudflare, ALB) and forward to the `web` container on port 8080. Set `APP_URL=https://crm.example.com` so:

- The CORS allow-list matches your real origin.
- Reset/confirm emails contain `https://` links.

Set `Cors__Origins__1=...` etc. via env if you need additional origins.

## Health & logs

- `GET /health` on the api container — used by compose's healthcheck.
- `docker compose logs -f api` — Serilog text output.
- Auth email outcomes appear as `Auth email (...) dispatched` or `Auth email (...) FAILED`.

## Production checklist

- [ ] `.env` populated; no secrets in image layers.
- [ ] SMTP sender verified at the provider.
- [ ] Backups configured for the `crm_data` volume (or DB outside Docker).
- [ ] `JWT_SECRET` rotated from the example value.
- [ ] TLS terminated at the edge.
- [ ] First admin password changed after first login.
- [ ] Twilio / OpenAI / Vici / Jornaya / GHL credentials added if those features are in scope.
