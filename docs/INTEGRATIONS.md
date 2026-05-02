# Integration setup

Every external integration has two implementations: a **Stub** (default, returns success without doing anything) and an **Http** real-vendor client. Switching between them is a single config change in `appsettings.json` (or environment variables — see `.NET configuration`).

> Do **not** commit real API keys. Either use `appsettings.Development.json` (gitignored), `dotnet user-secrets`, or environment variables.

---

## OpenAI (or any OpenAI-compatible LLM)

Powers `ICallSummarizer`, `ILeadAiScorer`, `IRecommendationService`. With the stub, calls return placeholder text. With OpenAI, summaries and scores are real.

```jsonc
"Ai": {
  "Provider": "OpenAI",
  "BaseUrl":  "https://api.openai.com",      // change to azure/groq/openrouter to use them
  "ApiKey":   "sk-...",                      // <- your key
  "Model":    "gpt-4o-mini"                  // any chat-completion-capable model
}
```

Or via env:
```bash
export Ai__Provider=OpenAI
export Ai__ApiKey=sk-...
```

Test:
```bash
curl -X POST http://localhost:5050/api/ai/calls/<callId>/summary \
  -H "Authorization: Bearer $TOKEN"
```

---

## SMS — Twilio

Powers the agent panel "Quick SMS", workflow `send-sms` action, and cadence SMS steps.

```jsonc
"Integrations": {
  "Sms": {
    "Provider":   "Twilio",
    "BaseUrl":    "https://api.twilio.com",
    "ApiKey":     "AC<accountSid>:<authToken>",   // colon-separated
    "FromNumber": "+15551234567"                  // your Twilio-purchased number
  }
}
```

Get credentials at https://console.twilio.com — free trial gives ~$15.50 of message credit.

Test:
```bash
curl -X POST http://localhost:5050/api/cc/calls/sms \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"leadId":"<lead-id>","body":"Hi from CRM"}'
```

---

## SMS — GoHighLevel

```jsonc
"Sms": {
  "Provider":   "GHL",
  "BaseUrl":    "https://services.leadconnectorhq.com",
  "ApiKey":     "<bearer-token>",
  "FromNumber": "+15551234567"
}
```

---

## Email — SMTP

```jsonc
"Email": {
  "Provider":    "Smtp",
  "SmtpHost":    "smtp.sendgrid.net",
  "SmtpPort":    587,
  "UseSsl":      true,
  "Username":    "apikey",
  "Password":    "<sendgrid-api-key>",
  "FromAddress": "no-reply@yourdomain.com",
  "FromName":    "Your CRM"
}
```

---

## Dialer — Vici

```jsonc
"Dialer": {
  "Provider": "Http",
  "BaseUrl":  "https://your-vicidial.example.com",
  "Username": "agent_api_user",
  "Password": "...",
  "Source":   "CRM"
}
```

The provider hits Vici's `agc/api.php?function=external_dial` and `external_hangup` endpoints. For coaching (listen/whisper/barge) you'll need to add `external_listen` — point me at the Vici you're targeting and I'll add the right URL.

---

## Jornaya pre-sale verification

```jsonc
"Jornaya": {
  "Provider":  "Http",
  "BaseUrl":   "https://api.jornaya.com",
  "AccountId": "<your-account-id>",
  "Token":     "<bearer-token>"
}
```

---

## BLA quote API

```jsonc
"Bla": {
  "Provider": "Http",
  "BaseUrl":  "https://your-bla-endpoint",
  "ApiKey":   "..."
}
```

---

## Trello card creation

```jsonc
"Trello": {
  "Provider": "Http",
  "Key":      "<api-key>",
  "Token":    "<token>"
}
```

Get both at https://trello.com/power-ups/admin.

---

## Carrier APIs (Aetna / UHC / etc.)

Each carrier needs its own endpoint:

```jsonc
"Carriers": {
  "Endpoints": {
    "AETNA": { "BaseUrl": "https://aetna.example.com", "ApiKey": "...", "TimeoutSeconds": 15 },
    "UHC":   { "BaseUrl": "https://uhc.example.com",   "ApiKey": "...", "TimeoutSeconds": 15 }
  }
}
```

To add a new carrier:
1. Subclass `HttpCarrierProvider` with the right `CarrierCode`
2. Register it in `DependencyInjection.cs` alongside Aetna/UHC
3. Add an endpoint entry in `appsettings.json`

---

## Background jobs — Hangfire

The default scheduler runs jobs in-process. For production (multiple instances, persistent jobs):

```jsonc
"BackgroundJobs": {
  "Provider": "Hangfire"
}
```

This enables the `/jobs` dashboard (Admin-only) at runtime. Backing store defaults to memory — switch to `UseSqlServerStorage` / `UseSqliteStorage` in `DependencyInjection.cs` for persistence.

---

## Webhooks — dialer signature verification

If your dialer signs its event webhooks:

```jsonc
"Webhooks": {
  "Dialer": { "Secret": "<shared-secret>" }
}
```

Then every `POST /api/webhooks/dialer` must include `X-Signature: <hex sha256>`.
