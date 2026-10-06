# Email deliverability — why invitations go to spam, and how to fix it

Status as of **6 Oct 2026**: every email this platform sends lands in spam, by design, and no
amount of code will change that. The fix is four DNS records and one config value, in that order.

---

## The problem, precisely

The app sends with:

```
From: saadsaqib869@gmail.com        via smtp-relay.brevo.com
```

A receiving server asks one question: *is this relay allowed to send as `gmail.com`?*

Google's answer is no. Brevo is not in gmail.com's SPF record, and Brevo signs with its own
domain, so DKIM cannot align with `gmail.com` either. The message therefore arrives claiming to be
from Gmail, unauthenticated, from a server that is not Gmail.

Since February 2024 both Gmail and Outlook treat that as near-certain spoofing. **This fails every
time.** It is not a sender-reputation problem that improves with volume or age.

## What the domain looks like today

```
$ dig +short TXT smhachieverslifegroup.com            # SPF
(nothing)

$ dig +short TXT _dmarc.smhachieverslifegroup.com
"v=DMARC1; p=quarantine; adkim=r; aspf=r; rua=mailto:dmarc_rua@onsecureserver.net;"

$ dig +short MX smhachieverslifegroup.com
(nothing)
```

Three things matter here:

1. **No SPF, no DKIM** on the domain.
2. **DMARC is already `p=quarantine`** — the registrar's default. It means: *anything claiming to be
   from this domain that fails authentication goes to spam.*
3. **No MX** — `support@smhachieverslifegroup.com`, which the app prints in every email, cannot
   receive a reply. Anyone who answers an invitation gets a bounce.

### ⚠️ The order is not optional

Because DMARC is already `p=quarantine`, switching `FromAddress` to
`@smhachieverslifegroup.com` **before** authentication is in place makes things strictly worse:
today's mail is merely suspicious, but that mail would be explicitly quarantined by our own policy.

**DNS first. Config second.**

---

## Fix, step by step

### 1. Authenticate the domain in Brevo

Brevo → **Settings → Senders, Domains & IPs → Domains** → add `smhachieverslifegroup.com` →
**Authenticate**.

Brevo shows a **Brevo code** TXT record and a **DKIM** record (selector `mail`, so the host is
`mail._domainkey`).

> **Copy those values from your own Brevo account.** They are account-specific. Do not copy a DKIM
> value or an SPF include from any guide, including this one — Brevo's published instructions have
> changed with the Sendinblue rebrand, and a stale `include:` silently authenticates nothing.

### 2. Add the records in Cloudflare

DNS for this domain is on Cloudflare (`mario.ns.cloudflare.com`, `samara.ns.cloudflare.com`).

Add each record exactly as Brevo displays it, and set **Proxy status: DNS only (grey cloud)**.
An orange cloud breaks TXT verification.

### 3. Add an MX record so replies work

Whatever mailbox provider you use for `support@smhachieverslifegroup.com` (Google Workspace,
Zoho, Fastmail) supplies its own MX records. Without them that address silently rejects everything.

### 4. Verify before changing any code

```bash
D=smhachieverslifegroup.com
dig +short TXT $D | grep -i spf          # SPF, if Brevo issued one
dig +short TXT mail._domainkey.$D        # DKIM — must return v=DKIM1
dig +short TXT _dmarc.$D                 # already present
dig +short MX  $D                        # replies
```

Brevo's Domains page should show a green tick against every row.

### 5. Then change the From address

Set, in `appsettings.Production.json` on the box (not in git — it holds the SMTP password):

```json
"Integrations": {
  "Email": {
    "FromAddress": "no-reply@smhachieverslifegroup.com",
    "FromName": "SMH Achievers Life Group",
    "SupportEmail": "support@smhachieverslifegroup.com"
  }
}
```

Restart the API. The startup check (below) will go quiet, which is how you know it took.

### 6. Prove it

Send an invitation to a Gmail address and an Outlook address. In Gmail, open the message →
**⋮ → Show original**. You want:

```
SPF:   PASS
DKIM:  PASS   with d=smhachieverslifegroup.com
DMARC: PASS
```

`d=` is the one people miss. If DKIM passes but `d=` is a Brevo domain rather than ours, it is not
aligned and DMARC still fails.

---

## The startup check

`CRM.Api/Startup/EmailDeliverabilityCheck.cs` runs on every boot outside Development and logs an
**error** when the From domain is not the app's domain, naming the consequence.

It warns; it never refuses to start. Mail in spam is bad — an API that will not come up is worse,
and an installation may legitimately be mid-migration between domains.

```bash
sudo journalctl -u crm-api -n 200 | grep "EMAIL DELIVERABILITY"
```

Covered by `CRM.Application.Tests/EmailDeliverabilityCheckTests.cs`, including the real shipped
configuration and a lookalike domain (`notsmhachieverslifegroup.com`) that the first version of the
check wrongly accepted.

---

## Why this went unnoticed

The From address has been a public Gmail mailbox since the first commit. Nothing in the code,
the config or the logs ever said that was a problem, and the symptom — one invitation in a junk
folder — looked like a one-off rather than a structural fault. The check above exists so the next
installation is told on its first boot.
