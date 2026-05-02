# CRM Platform

Multi-role insurance tele-sales CRM. Backend in .NET 10 (clean architecture, EF Core, ASP.NET Identity, JWT + TOTP 2FA, SignalR). Frontend in React 18 + TypeScript + Vite + Redux Toolkit + Tailwind.

## Status — Day 1 foundation complete

Implemented:
- Solution scaffolding: Domain / Application / Infrastructure / API + three test projects.
- Domain entities: Agency, Team, IpAllowlistEntry, Lead, LeadActivity, ScheduledCallback, Sale, AuditEntry, Notification.
- Workflow stage state machine (New → Fronted → Verified → JrClosed → Closed → Validated → Funded → Followup → Winback / Lost) with unit tests (10/10 passing).
- CQRS via MediatR with FluentValidation + Logging pipeline behaviors.
- ASP.NET Identity + JWT access (15 min) + refresh (7 d) tokens, refresh-token hashing, rotation and revocation.
- TOTP 2FA (Google Authenticator-compatible), pending-token flow on login.
- EF Core audit interceptor (writes change-set JSON to `AuditLog` for every entity mutation).
- IP allowlist middleware (skips loopback + auth endpoints + Swagger).
- Global exception middleware mapping domain exceptions → ProblemDetails.
- SignalR PresenceHub for agent status broadcasting.
- Role-seeded admin user, default agency, and database migration on startup.
- React shell with login + 2FA enrollment, protected routes, role-gated nav, leads CRUD + stage transitions, users list, refresh-token interceptor.

## Running

### Backend
```bash
cd backend
dotnet build
dotnet run --project src/CRM.Api --urls=http://localhost:5050
```
Swagger: http://localhost:5050/swagger
Default admin: `admin` / `Admin@123!`

### Frontend
```bash
cd frontend
npm install
npm run dev
```
Open http://localhost:5173 — login with the default admin.

### Tests
```bash
cd backend
dotnet test
```

## Project layout

```
backend/
  src/
    CRM.Domain/         entities, enums, base types
    CRM.Application/    use cases (MediatR), DTOs, validators, interfaces
    CRM.Infrastructure/ EF Core, Identity, JWT, TOTP, audit
    CRM.Api/            controllers, middleware, hubs, Program.cs
  tests/
    CRM.Domain.Tests/
    CRM.Application.Tests/   (workflow state-machine tests)
    CRM.Api.IntegrationTests/

frontend/
  src/
    app/                router, Redux store
    features/           auth, leads, users
    shared/api/         RTK Query baseApi, types
    shared/components/  Layout, ProtectedRoute
    pages/              Dashboard
```

## Roadmap

Module priorities (full plan: `~/.claude/plans/crm-workflows-and-dreamy-moler.md`):

| Phase | Modules |
|---|---|
| Day 2–3 | Marketing UI (Fronter / Verifier), lead-queue assignment |
| Day 4–5 | Operations UI (Closer / Validator), internal-sale checker |
| Day 6 | Jornaya pre-sale + Vici dialer integration scaffolds |
| Day 7–8 | Retention, scheduled callbacks + reminders, notifications |
| Day 9–10 | KPI dashboards, management views, custom verticals |
| Beyond | BLA, carriers, SMS, payroll, QA module, chat, policy funding |

## Key configuration (`backend/src/CRM.Api/appsettings.json`)

- `ConnectionStrings:Default` — defaults to `Data Source=crm.db` (SQLite). Switch `Database:Provider` to `SqlServer` and supply a SQL Server connection string for production.
- `Jwt:Secret` — replace with a 256-bit secret from your secret manager before deploying.
- `Cors:Origins` — list of allowed origins for the SPA.

## Verification (Day 1)

- `dotnet build` → 0 errors.
- `dotnet test` → 10/10 workflow transition tests pass.
- `npm run build` → frontend bundles cleanly (~370 KB JS, ~9 KB CSS).
- Manual smoke (logged via curl in this session):
  - `GET /health` → 200
  - `POST /api/auth/login` (admin) → JWT + refresh token
  - `GET /api/auth/me` (with Bearer) → user summary with admin role
  - `POST /api/leads` → creates lead in `New` stage
  - `GET /api/leads` → returns the lead
  - `GET /api/leads` (no Authorization header) → 401
