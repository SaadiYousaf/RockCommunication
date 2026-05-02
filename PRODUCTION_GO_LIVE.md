# Apex CRM — Production Go-Live Checklist

This is the **complete shopping list** to take this CRM from dev/stub mode to a
live, production-grade system. Each line tells you:

1. **What to buy / sign up for** (with realistic pricing).
2. **Where the credentials go** (exact JSON path in `appsettings.json` or env-var name).
3. **Whether it's required** (the app keeps working in stub mode if you skip an
   optional integration).

> **Where to put the keys** — see [§ Where credentials go](#-where-credentials-go) at the bottom for two
> recommended approaches: editing `appsettings.Production.json` (fastest) or
> using environment variables / Azure Key Vault / AWS Secrets Manager (proper).

---

## 0. Quick verdict

| Tier | What you must buy | Approx monthly cost |
|---|---|---|
| **Bare minimum to launch** | Hosting + Postgres/SQL Server + Email (SMTP) | **$60-150 / mo** |
| **Recommended for a real call-center** | + Vici dialer + Twilio SMS + Redis + Domain/SSL | **$200-400 / mo** + per-message |
| **Fully featured (TCPA-compliant US insurance)** | + Jornaya + carrier APIs + monitoring + backups | **$500-1,000+ / mo** |

---

## 1. Required infrastructure (you can't skip these)

### 1.1 Application hosting

You need somewhere to run the .NET 10 backend and the React frontend.

| Option | Effort | Cost | Notes |
|---|---|---|---|
| **DigitalOcean / Hetzner / Vultr VPS** | low | $20-80/mo | One $40 VPS handles ~500 concurrent users |
| **AWS (ECS / Elastic Beanstalk)** | medium | $80-200/mo | Auto-scales, pricier |
| **Azure App Service + Azure SQL** | low | $100-250/mo | Easiest for .NET — Microsoft-native |
| **Render / Fly.io / Railway** | very low | $30-100/mo | Modern PaaS, deploys from git |

**Recommendation**: Azure App Service for the .NET API + Azure Static Web Apps for the React frontend. Lowest friction for .NET 10.

### 1.2 Database (REQUIRED)

The app supports SQLite (dev only), SQL Server, and Postgres.

| Option | Cost | Where credentials go |
|---|---|---|
| **Azure SQL Database — General Purpose** | $15-200/mo | `ConnectionStrings:Default` |
| **AWS RDS Postgres** | $25-150/mo | same |
| **DigitalOcean Managed Postgres** | $15-60/mo | same |
| **Self-hosted Postgres** (free, your VPS) | $0 (included with VPS) | same |

**Where it goes** in `appsettings.Production.json`:
```jsonc
{
  "ConnectionStrings": {
    "Default": "Host=your-db.example.com;Database=crm;Username=crm;Password=YOUR_DB_PASSWORD;SSL Mode=Require;Trust Server Certificate=true"
  },
  "Database": {
    "Provider": "Postgres"   // "SqlServer" | "Postgres" | "Sqlite"
  }
}
```

### 1.3 Email — SMTP provider (REQUIRED for password resets, 2FA emails, customer outreach)

| Provider | Free tier | Paid | Best for |
|---|---|---|---|
| **Brevo (Sendinblue)** | 300/day free | $9-25/mo | Already configured in dev. Easiest. |
| **Postmark** | 100 free | $15/mo for 10k | Best transactional deliverability |
| **AWS SES** | $0.10 per 1,000 | pay-as-you-go | Cheapest at volume |
| **SendGrid** | 100/day free | $20/mo | Most popular |
| **Office 365 / Google Workspace SMTP** | bundled | bundled | Already paying for it |

**Where it goes**:
```jsonc
{
  "Integrations": {
    "Email": {
      "Provider": "Smtp",
      "SmtpHost": "smtp-relay.brevo.com",
      "SmtpPort": 587,
      "UseSsl": true,
      "Username": "YOUR_SMTP_LOGIN",
      "Password": "YOUR_SMTP_PASSWORD",
      "FromAddress": "noreply@yourdomain.com",
      "FromName": "Apex CRM",
      "AppUrl": "https://your-crm.example.com",
      "SupportEmail": "support@yourdomain.com"
    }
  }
}
```

### 1.4 Domain + SSL (REQUIRED)

- **Domain**: Namecheap / Cloudflare Registrar — $10-20/yr
- **SSL**: Cloudflare (free) or Let's Encrypt (free), or your hosting provider auto-issues one
- Point your domain at the API (`api.yourdomain.com`) and the frontend (`app.yourdomain.com`)

Update CORS in `appsettings.Production.json`:
```jsonc
{
  "Cors": {
    "Origins": [ "https://app.yourdomain.com" ]
  }
}
```

### 1.5 JWT signing secret (REQUIRED)

Generate a 256-bit random string and put it here. **Never commit it.**

```bash
# Linux/Mac — generate one:
openssl rand -base64 48
```

```jsonc
{
  "Jwt": {
    "Issuer":   "https://api.yourdomain.com",
    "Audience": "https://app.yourdomain.com",
    "Secret":   "PASTE_THE_OPENSSL_OUTPUT_HERE",
    "AccessTokenMinutes": 15,
    "RefreshTokenDays": 7
  }
}
```

---

## 2. Recommended for a real call-center

### 2.1 Telephony / dialer (Vici) — REQUIRED if you take calls

| Option | Cost | Setup difficulty |
|---|---|---|
| **Self-hosted ViciDial on a $20 VPS** | $20-40/mo + SIP carrier minutes | High — needs Linux skills |
| **Managed Vici provider** (e.g. CallShaper, Convoso) | $100-300/seat/mo | Low — they configure it |
| **Twilio Programmable Voice + custom integration** | pay-per-minute | Medium |

**Where it goes**:
```jsonc
{
  "Integrations": {
    "Dialer": {
      "Provider":  "Vici",                          // or "Http" for any dialer with HTTP API
      "BaseUrl":   "https://your-vici-server.com",
      "Username":  "vici_api_user",
      "Password":  "VICI_API_PASSWORD",
      "Source":    "CRM",
      "TimeoutSeconds": 10
    }
  },
  "Webhooks": {
    "Dialer": { "Secret": "GENERATE_A_RANDOM_SECRET_HERE" }
  }
}
```

The webhook secret protects `/api/webhooks/dialer/*` — set it to a long random string and configure it in the Vici outbound webhook settings too.

### 2.2 SMS (Twilio or GoHighLevel)

| Provider | Cost | Setup |
|---|---|---|
| **Twilio** | $0.0079/SMS US, $1/mo per number | Sign up → buy a phone number → grab API key |
| **GoHighLevel** | $97-297/mo all-inclusive | Sign up → get API URL + key from their dashboard |

**Where it goes**:
```jsonc
// Twilio
{
  "Integrations": {
    "Sms": {
      "Provider":   "Twilio",
      "BaseUrl":    "",
      "ApiKey":     "ACxxxxxxxx:AUTH_TOKEN",       // {SID}:{Token}
      "FromNumber": "+15555550100"
    }
  }
}

// or GoHighLevel
{
  "Integrations": {
    "Sms": {
      "Provider":   "GHL",
      "BaseUrl":    "https://services.leadconnectorhq.com",
      "ApiKey":     "GHL_API_KEY",
      "FromNumber": "+15555550100"
    }
  }
}
```

### 2.3 Redis (REQUIRED for >1 API replica)

If you ever run more than one API instance (load-balanced for scale), you need Redis for:
- SignalR backplane (chat / supervisor / agent push events across replicas)
- Hangfire job storage (optional — Postgres/SQL Server also work)

| Option | Cost |
|---|---|
| **DigitalOcean Managed Redis** | $15/mo |
| **AWS ElastiCache** | $25-100/mo |
| **Upstash Redis** | $0-10/mo (pay-per-request) |
| **Self-hosted on the same VPS** | $0 |

**Where it goes**:
```jsonc
{
  "SignalR": {
    "Backplane": "Redis",
    "Redis": {
      "ConnectionString": "your-redis-host:6379,password=YOUR_REDIS_PW,abortConnect=false",
      "Channel": "crm:signalr"
    }
  }
}
```

### 2.4 Background jobs storage (Hangfire)

Single-instance: skip — Memory storage is fine.
Multi-instance: switch to durable storage so jobs survive restarts.

```jsonc
{
  "BackgroundJobs": {
    "Provider": "Hangfire",
    "Storage":  "Postgres",                                   // or "SqlServer"
    "ConnectionString": "Host=...;Database=crm_jobs;...",     // optional, falls back to Default
    "Workers": 16
  }
}
```

---

## 3. Optional integrations (insurance / lead-gen specific)

### 3.1 Jornaya (LeadiD) — TCPA compliance

Required if you call US leads and want litigation protection. Without it the
CRM still works, but you carry the risk.

- **Sign up**: https://www.jornaya.com — they require a sales call
- **Cost**: ~$0.05-0.15 per lookup, contracted

```jsonc
{
  "Integrations": {
    "Jornaya": {
      "Provider":  "Http",
      "BaseUrl":   "https://api.leadid.com",
      "AccountId": "YOUR_JORNAYA_ACCOUNT_ID",
      "Token":     "YOUR_JORNAYA_TOKEN",
      "TimeoutSeconds": 10
    }
  }
}
```

### 3.2 Carrier APIs (Aetna / UnitedHealth / Cigna / etc.)

Free once you're a contracted/appointed agent with the carrier. Each one gives
you a base URL + API key.

```jsonc
{
  "Integrations": {
    "Carriers": {
      "Endpoints": {
        "AETNA": {
          "BaseUrl": "https://api.aetna.com/v1",
          "ApiKey":  "AETNA_KEY",
          "TimeoutSeconds": 15
        },
        "UHC": {
          "BaseUrl": "https://api.uhc.com/v1",
          "ApiKey":  "UHC_KEY",
          "TimeoutSeconds": 15
        }
      }
    }
  }
}
```

If you leave `Endpoints` empty (`{}`), the CRM uses stub carriers that return
realistic mock quotes.

### 3.3 Funding provider

If you have a funding provider that issues a contracted API, point at it here.
Otherwise the stub auto-accepts everything (good for dev / training).

```jsonc
{
  "Integrations": {
    "Funding": {
      "Provider": "Http",
      "BaseUrl":  "https://api.your-funding-provider.com",
      "ApiKey":   "FUNDING_API_KEY"
    }
  }
}
```

### 3.4 BLA (lead-quality scoring)

Skip unless you specifically buy from BLA.

```jsonc
{
  "Integrations": {
    "Bla": {
      "Provider": "Http",
      "BaseUrl":  "https://api.bla-vendor.com",
      "ApiKey":   "BLA_API_KEY",
      "TimeoutSeconds": 10
    }
  }
}
```

### 3.5 Trello (ops board sync)

Optional — only useful if your ops team already lives in Trello.

- **Get a key**: https://trello.com/app-key
- **Generate a token**: same page → "Token" link

```jsonc
{
  "Integrations": {
    "Trello": {
      "Provider": "Http",
      "Key":      "TRELLO_KEY",
      "Token":    "TRELLO_TOKEN",
      "BaseUrl":  "https://api.trello.com/1"
    }
  }
}
```

---

## 4. Operational essentials

### 4.1 Backups

- Managed Postgres / SQL Server providers do automated daily backups — turn it on, set retention to ≥ 30 days.
- For self-hosted DBs, set up `pg_dump` or `sqlcmd` on a cron and ship the dumps to S3 / Backblaze B2 (~$5/mo).

### 4.2 Monitoring + error tracking

| Tool | Free tier | Best for |
|---|---|---|
| **Application Insights** (Azure) | 5 GB / mo | .NET-native, deep request tracing |
| **Datadog** | trial | full-stack APM |
| **Sentry** | 5k events/mo | error tracking specifically |
| **Better Stack / Logtail** | 1 GB free | log aggregation only |

For Sentry (cheapest, easiest):
```jsonc
{
  "Sentry": {
    "Dsn": "https://xxx@sentry.io/yyy",
    "Environment": "production"
  }
}
```
*(Add the Sentry SDK package to enable — not currently wired.)*

### 4.3 Rate limits (already wired, just tune)

```jsonc
{
  "RateLimits": {
    "User":    { "Burst": 240, "Refill": 120, "RefillSeconds": 60 },
    "Anon":    { "Burst": 60,  "Refill": 30,  "RefillSeconds": 60 },
    "Auth":    { "PerMinute": 5 },
    "Webhook": { "PerMinute": 1200 }
  },
  "Pagination": { "MaxTake": 200 }
}
```

### 4.4 IP allow-list (optional but recommended for admin endpoints)

The app has IP-allowlist middleware. Configure ranges via the in-app **Admin → IP Allowlist** page once you're signed in.

---

## 5. Where credentials go

You have **three** options, in increasing order of correctness:

### Option A — `appsettings.Production.json` (simplest, fine for small deployments)

Create the file at `backend/src/CRM.Api/appsettings.Production.json` and put **all of section 1-3** there. **Never commit it** — add it to `.gitignore`.

```bash
echo "appsettings.Production.json" >> backend/src/CRM.Api/.gitignore
```

ASP.NET Core auto-loads it when `ASPNETCORE_ENVIRONMENT=Production`.

### Option B — Environment variables (recommended for cloud)

ASP.NET Core maps env vars to config keys with `:` → `__`. Examples:

```bash
ConnectionStrings__Default="Host=...;..."
Jwt__Secret="abc..."
Integrations__Email__Password="smtp-pw"
Integrations__Sms__ApiKey="ACxxx:token"
Integrations__Dialer__Password="vici-pw"
Webhooks__Dialer__Secret="random-secret"
SignalR__Redis__ConnectionString="redis-host:6379,password=..."
BackgroundJobs__ConnectionString="Host=...;..."
```

Set them in your cloud provider's "Application Settings" / "Environment" panel. Restart the API to apply.

### Option C — Secret manager (correct for compliance)

- **Azure Key Vault** — first-class .NET integration, ~$0.03 per 10k operations
- **AWS Secrets Manager** — $0.40/secret/mo
- **Doppler / HashiCorp Vault** — multi-cloud

The app's existing `IConfiguration` plumbing supports all of them via standard
ASP.NET Core configuration providers — no code changes needed.

---

## 6. Final go-live checklist

- [ ] Domain purchased + DNS pointed at server
- [ ] SSL cert installed (Let's Encrypt / Cloudflare)
- [ ] Production Postgres or SQL Server created with daily backups
- [ ] `Jwt:Secret` generated and stored
- [ ] SMTP creds in (`Integrations:Email`) — test by triggering a password-reset email
- [ ] CORS origins updated to your real domain
- [ ] Vici / SMS / Jornaya / Carrier creds added (whichever you bought)
- [ ] Webhook secret set on both sides (CRM + dialer admin)
- [ ] Redis provisioned if running >1 replica
- [ ] Background jobs flipped to durable storage if running >1 replica
- [ ] Rate-limit + pagination caps reviewed and tuned for expected load
- [ ] Backups verified by restoring once into a staging DB
- [ ] Monitoring (App Insights / Sentry / Datadog) wired up
- [ ] First admin user created with a strong password (or use the seeded `superadmin` and **change its password immediately**)
- [ ] `Seed:DummyData` set to `false` in production so test data isn't generated
- [ ] `ASPNETCORE_ENVIRONMENT=Production` env var set so Swagger UI is hidden

---

## 7. Quick answer: minimum viable purchase list

If you just want **the cheapest possible go-live**:

1. **VPS** — DigitalOcean droplet, $24/mo
2. **Database** — same droplet, self-hosted Postgres, $0
3. **Domain** — Cloudflare, $10/yr
4. **SSL** — Cloudflare, free
5. **Email** — Brevo free tier (300/day) → $9/mo paid when you grow
6. **Twilio** — pay-per-SMS, sign up for free
7. **Vici** — self-host on a $20 droplet OR sign up with a managed Vici provider when you're ready

Total fixed cost for month 1: **~$25-35**. Add per-SMS/per-call charges as you go.

---

## 8. Help

- For the integration health page in the app, sign in as Admin and visit
  **/admin/integrations** — it shows every integration's live/stub status
  and lets you run a config-validity check.
- For background-job monitoring (queue depth, failures), visit **/jobs**
  (Hangfire dashboard) — Admin role required.
- All config keys above are read at startup. After changing any of them,
  **restart the API** for them to take effect.
