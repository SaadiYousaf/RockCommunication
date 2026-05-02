# Deploy Apex CRM to `makbpo.net`

End-to-end steps from your laptop → live HTTPS site at `https://makbpo.net`.

---

## 1. Buy / pick a server (~10 min)

You need a Linux VPS. Cheapest acceptable specs: **2 vCPU, 4 GB RAM, 40 GB SSD**.

| Provider | Plan | Cost | Why |
|---|---|---|---|
| **DigitalOcean** | "Premium 4 GB" droplet (Ubuntu 24.04) | $24/mo | Simplest, great docs |
| **Hetzner** | CX22 (Ubuntu 24.04) | €4.51/mo | Cheapest reliable option |
| **Vultr** | "High Frequency 4 GB" | $24/mo | Good for US users |
| **AWS Lightsail** | 4 GB plan | $20/mo | If you already use AWS |

After signup:
1. Create the droplet/VPS in a region close to your users
2. Add your **SSH public key** (so you don't have to type passwords)
3. Note the public IPv4 address — e.g. `137.184.X.X`. **You'll need it in step 2.**

---

## 2. Point `makbpo.net` at the server (~5 min, takes 5-30 min to propagate)

In GoDaddy → **Domain Portfolio → makbpo.net → DNS**:

1. **Delete** any existing `A` record for `@` and any `CNAME` for `www`.
2. **Add** these two records:

| Type  | Name | Value (use your server's IP) | TTL  |
|-------|------|------------------------------|------|
| A     | `@`  | `137.184.X.X`                | 600  |
| A     | `www`| `137.184.X.X`                | 600  |

That's it — don't use GoDaddy's "Connect to a website" wizard from your screenshot. Those wizards are for their hosted site builders. **Skip the whole "Let's connect" screen** and go straight to the DNS records page.

Verify (from your laptop):
```bash
dig +short makbpo.net      # should print 137.184.X.X
dig +short www.makbpo.net  # same IP
```

If it shows the old IP, wait 10 min and re-run.

---

## 3. Prepare the server (~10 min)

SSH in as root:
```bash
ssh root@137.184.X.X
```

Install Docker + git + a firewall:
```bash
# Docker
curl -fsSL https://get.docker.com | sh

# Open the ports we need
ufw allow OpenSSH
ufw allow 80/tcp
ufw allow 443/tcp
ufw enable
```

Create a non-root user (optional but recommended):
```bash
adduser deploy
usermod -aG docker deploy
usermod -aG sudo  deploy
# copy your ssh key
rsync --archive --chown=deploy:deploy ~/.ssh /home/deploy
exit
ssh deploy@137.184.X.X
```

---

## 4. Get the code onto the server (~5 min)

Two options:

**A. Push to GitHub, clone on the server** (recommended):
```bash
# On the server:
cd /opt
sudo mkdir crm && sudo chown $USER:$USER crm && cd crm
git clone https://github.com/your-org/your-crm.git .
```

**B. Copy from your laptop directly** (works without git):
```bash
# From your laptop:
cd ~/Documents/CRM
rsync -avz --exclude node_modules --exclude bin --exclude obj --exclude .git \
  ./ deploy@137.184.X.X:/opt/crm/
```

---

## 5. Generate `.env` for production secrets (~5 min)

On the server, in `/opt/crm`:

```bash
# Generate a long random JWT secret
JWT=$(openssl rand -base64 48)

cat > .env <<EOF
# ─── Public URL the app is served from ───
APP_URL=https://makbpo.net
WEB_PORT=80     # we'll add HTTPS in front of this in step 7

# ─── JWT (256-bit secret, never commit) ───
JWT_SECRET=$JWT
JWT_ISSUER=https://makbpo.net
JWT_AUDIENCE=https://makbpo.net
JWT_ACCESS_MINUTES=15
JWT_REFRESH_DAYS=7

# ─── Database (defaults to SQLite on a docker volume) ───
DATABASE_PROVIDER=Sqlite
CONNECTION_STRING=Data Source=/data/crm.db

# ─── Email (Brevo / SendGrid / SES — use real creds) ───
EMAIL_PROVIDER=Smtp
SMTP_HOST=smtp-relay.brevo.com
SMTP_PORT=465
SMTP_USERNAME=YOUR_BREVO_LOGIN
SMTP_PASSWORD=YOUR_BREVO_KEY
EMAIL_FROM=noreply@makbpo.net
EMAIL_FROM_NAME=MAK BPO
SUPPORT_EMAIL=support@makbpo.net

# ─── Optional vendors (leave Stub until you've signed up) ───
SMS_PROVIDER=Stub
SMS_API_KEY=
SMS_FROM=

AI_PROVIDER=Stub
AI_API_KEY=
AI_MODEL=gpt-4o-mini
EOF

chmod 600 .env       # only owner can read it
```

> **About the email creds**: You already had Brevo configured in dev. Get the same login/key from your Brevo dashboard (`SMTP & API` section) and paste them here.

---

## 6. First boot (~5 min)

```bash
cd /opt/crm
docker compose up -d --build
```

Watch the logs until both containers are healthy:
```bash
docker compose ps          # both should say "healthy"
docker compose logs -f api # Ctrl+C when you see "Application started"
```

Test it works on plain HTTP first:
```bash
curl -I http://makbpo.net/health   # should return 200
```

If that works, you have the CRM running on port 80 — but **without HTTPS** yet.

---

## 7. Add HTTPS — the **single most important step** (~10 min)

Browsers reject auth cookies / SignalR over plain HTTP, and your JWT secret will leak in transit. We'll put **Caddy** in front (handles Let's Encrypt automatically — no certbot config needed).

Stop the current stack:
```bash
docker compose down
```

Edit `docker-compose.yml` — change the `web` service `ports` from `8080` to an internal-only `expose` and add a Caddy service. Append this to the bottom of `docker-compose.yml`:

```yaml
  caddy:
    image: caddy:2-alpine
    container_name: crm-caddy
    restart: unless-stopped
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - caddy_data:/data
      - caddy_config:/config
    depends_on:
      - web

volumes:
  crm_data:
  caddy_data:
  caddy_config:
```

Change the `web:` service to expose internally:
```yaml
  web:
    # …existing config…
    ports: []          # remove the public 80 mapping
    expose:
      - "8080"
```

Create `/opt/crm/Caddyfile`:
```caddyfile
makbpo.net, www.makbpo.net {
    encode zstd gzip

    # Security headers
    header {
        Strict-Transport-Security "max-age=31536000; includeSubDomains; preload"
        X-Content-Type-Options    "nosniff"
        X-Frame-Options           "DENY"
        Referrer-Policy           "strict-origin-when-cross-origin"
        -Server
    }

    # Reverse-proxy everything to the web container (which itself proxies /api → api)
    reverse_proxy web:8080

    # Redirect www → apex
    @www host www.makbpo.net
    redir @www https://makbpo.net{uri} permanent
}
```

Update `.env` so `APP_URL` reflects HTTPS:
```bash
sed -i 's|APP_URL=.*|APP_URL=https://makbpo.net|' .env
```

Bring it back up:
```bash
docker compose up -d --build
```

Caddy will automatically request a Let's Encrypt cert the first time it sees a request for `makbpo.net`. You should see in `docker compose logs caddy` something like *"certificate obtained successfully"*.

Test:
```bash
curl -I https://makbpo.net/health     # 200 OK
curl -I http://makbpo.net/            # 308 redirect to https
```

Open `https://makbpo.net` in the browser — you should see the login page with a green padlock.

---

## 8. First admin login + lockdown (~5 min)

The seeder created two default accounts:
- `admin / Admin@123!`
- `superadmin / SuperAdmin@123!`

**Sign in as `superadmin`** at `https://makbpo.net/login`, then **immediately**:

1. Go to **Account → Change password** and set a strong one for both default accounts.
2. Go to **Admin → User Management** and create your real admin user.
3. **Disable** the default `admin` and `superadmin` accounts once your real user works.
4. (Optional but recommended) Enable 2FA for the new admin user.

Then turn off seed-data generation so dummy data isn't re-injected on a fresh DB:
```bash
# Add to .env:
echo "Seed__DummyData=false" >> .env
docker compose up -d
```

---

## 9. Backups (~5 min — once)

The DB lives in the named volume `crm_data` at `/data/crm.db`. Set up a nightly snapshot:

```bash
sudo tee /etc/cron.daily/crm-backup > /dev/null <<'EOF'
#!/bin/bash
set -e
DEST=/var/backups/crm
mkdir -p "$DEST"
docker compose -f /opt/crm/docker-compose.yml exec -T api \
  sh -c 'sqlite3 /data/crm.db ".backup /data/backup.db" && cat /data/backup.db' \
  > "$DEST/crm-$(date +%Y%m%d).db"
# keep last 30 days
find "$DEST" -name "crm-*.db" -mtime +30 -delete
EOF
sudo chmod +x /etc/cron.daily/crm-backup
```

---

## 10. Day-2 operations cheat sheet

```bash
# Tail logs
docker compose logs -f api
docker compose logs -f caddy

# Restart after .env changes
docker compose up -d

# Pull latest code & redeploy
cd /opt/crm && git pull && docker compose up -d --build

# Stop everything
docker compose down

# Check resource usage
docker stats

# Renew Caddy cert manually (it auto-renews but if you ever need to)
docker compose restart caddy
```

---

## What can go wrong

| Symptom | Fix |
|---|---|
| `dig` still shows old IP after 30 min | Lower TTL on the GoDaddy DNS record to 600, wait again |
| Caddy can't get a cert | Ensure ports 80 + 443 are open in `ufw` AND in your VPS provider's firewall panel |
| API container restarts | `docker compose logs api` — usually a missing `JWT_SECRET` in `.env` |
| 502 from Caddy | `docker compose ps` — the `api` container is unhealthy. Check its logs |
| CORS error on login | `APP_URL` in `.env` must match the URL you're loading the app from |
| Login works but /api/auth/me 401 immediately | Clock skew — run `timedatectl set-ntp true` on the server |

---

## Summary

In order:
1. **Buy a $24/mo VPS**, note its IP
2. **GoDaddy → DNS**: add A records for `@` and `www` pointing at that IP
3. **SSH in**, install Docker, open firewall ports
4. **Copy your code** to `/opt/crm`
5. **Generate `.env`** with a real JWT secret + Brevo SMTP creds
6. `docker compose up -d --build` — basic boot
7. **Add Caddy + Caddyfile** for automatic HTTPS via Let's Encrypt
8. **Sign in as `superadmin`**, change passwords, create your real admin
9. **Set up nightly DB backups**
10. Done — `https://makbpo.net` is live

Total wall-clock time: **about 45 minutes** if everything goes smoothly.
