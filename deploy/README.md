# Deploying the AlertMe API to a VPS

```
Browser / mobile app
        │ https://api.<your-domain>
        ▼
   Cloudflare edge (TLS, DDoS, WAF)
        ▲
        │ outbound tunnel – the VPS opens NO web ports
┌───────┴──────────────── VPS (Ubuntu) ────────────────────────┐
│  cloudflared ──edge net──► api:8080 ──db net──► mysql:3306   │
│                                                 ▲            │
│  UFW: only 22/tcp (keys only, fail2ban)         127.0.0.1    │
└──────────────────────────────────────────────────────────────┘
```

Files in this folder:

| File | Where it ends up |
|---|---|
| `harden.sh` | run once as root on a fresh server |
| `docker-compose.yml` | used in place from `/opt/alertme/app/deploy` |
| `.env.example` → `.env` | DB passwords, tunnel token, frontend origin |
| `api.env.example` → `api.env` | app secrets (JWT, Telegram, Brevo, …) |
| `deploy-alertme.sh` | `/usr/local/bin/deploy-alertme.sh`, run by GitHub Actions |
| `backup-mysql.sh` | `/usr/local/bin/backup-alertme.sh`, run nightly by cron |

---

## 0. The old server is burned – rotate everything

Assume the attacker read every file on the old box, including its `.env`. Before going live, create
**new** values for all of these. Don't reuse any old ones:

- [ ] `Jwt__Key` (every token signed with the old key can be forged)
- [ ] Telegram bot token: `/revoke` in @BotFather
- [ ] Brevo API key, WorldNews API key, Expo access token
- [ ] MySQL passwords
- [ ] The GitHub Actions SSH key (`CONTABO_SSH_KEY`). Delete the old secret
- [ ] Google OAuth client secret, if the old server had it
- [ ] Any SSH key that was ever on the old server

Copy **nothing** from the old server: no scripts, no binaries, no crontabs. If you need the old data,
restore a DB dump and then check the users/roles tables for accounts you don't recognise.

Common ways boxes like the old one get in: SSH with a password, MySQL or Ollama published with
`-p 3306:3306` / `-p 11434:11434` (Docker punches through UFW), or a leaked `.env`. This setup closes
all three.

---

## 1. Make an SSH key on your laptop (PowerShell)

```powershell
ssh-keygen -t ed25519 -C "lasal-laptop"      # set a passphrase
Get-Content $HOME\.ssh\id_ed25519.pub         # copy this line
```

## 2. Harden the server

```powershell
scp deploy\harden.sh root@<VPS-IP>:/root/
ssh root@<VPS-IP>
```

On the server:

```bash
passwd                      # the provider emailed you the root password, so change it
bash /root/harden.sh lasal "ssh-ed25519 AAAA...your key... lasal-laptop"
```

**Keep that root session open.** In a *new* terminal, check that `ssh lasal@<VPS-IP>` works and
`sudo -v` accepts your password. Only then close the root session. From now on root and password
logins are refused.

What the script did: it upgraded packages and created the `lasal` user (sudo) and the `deploy` user
(docker, no sudo). It set SSH to keys only, with no root login and `AllowUsers`. It enabled UFW (only
SSH in, rate-limited), fail2ban, and automatic security updates. It installed Docker with log rotation
and `no-new-privileges`.

## 3. Create the Cloudflare Tunnel

Your domain's DNS must be on Cloudflare.

1. Go to **Zero Trust → Networks → Tunnels → Create a tunnel → Cloudflared**, and name it `alertme-api`.
2. On the install screen, copy only the token (the long `eyJ...` string after `--token`). You don't
   need to install anything. The compose stack runs cloudflared.
3. Add a public hostname (also called a published application route): subdomain `api`, your
   domain, service type **HTTP**, URL **`api:8080`**.

## 4. Get the code onto the server

As your admin user:

```bash
sudo -iu deploy
ssh-keygen -t ed25519 -f ~/.ssh/github -N "" -C "alertme-vps-deploy"
cat ~/.ssh/github.pub
```

In GitHub, go to **dot-net-app → Settings → Deploy keys → Add**. Paste the key and leave
**write access unticked**.

```bash
cat >> ~/.ssh/config <<'EOF'
Host github.com
  IdentityFile ~/.ssh/github
  IdentitiesOnly yes
EOF
ssh -T git@github.com            # accept the host key; it should greet you by repo name
git clone git@github.com:LasalJayasinghe/dot-net-app.git /opt/alertme/app
```

## 5. Fill in the secrets (still as `deploy`)

```bash
cd /opt/alertme/app/deploy
cp .env.example .env && cp api.env.example api.env
chmod 600 .env api.env
openssl rand -hex 32             # run once per password
openssl rand -base64 48          # for Jwt__Key
nano .env                        # MYSQL_*, TUNNEL_TOKEN, FRONTEND_ORIGIN
nano api.env                     # AllowedHosts, Jwt__Key, Telegram, Brevo, ...
```

## 6. Start it

```bash
docker compose up -d --build
docker compose ps
docker compose logs -f api
```

The database is empty on the first start, so the role seeder fails and `api` restarts in a loop.
That's expected until you complete step 7.

## 7. Apply EF migrations from your laptop

MySQL listens only on the server's loopback, so open an SSH tunnel to it:

```powershell
# terminal 1 – leave running
ssh -N -L 3307:127.0.0.1:3306 lasal@<VPS-IP>

# terminal 2 – in dot-net-app/. A real env var beats .env, so this targets the server DB.
$env:ConnectionStrings__DefaultConnection = "Server=127.0.0.1;Port=3307;Database=alertMe;User=alertme;Password=<MYSQL_PASSWORD>;"
dotnet ef database update
Remove-Item Env:ConnectionStrings__DefaultConnection
```

The `api` container recovers by itself on its next restart. Check it:

```bash
curl https://api.<your-domain>/health      # → Healthy
```

You can point MySQL Workbench or DBeaver at `127.0.0.1:3307` through the same tunnel.

## 8. Wire up GitHub Actions

On your laptop, create a separate key just for CI (press Enter twice for no passphrase):

```powershell
ssh-keygen -t ed25519 -f $HOME\.ssh\alertme-ci -C "github-actions"
Get-Content $HOME\.ssh\alertme-ci.pub
```

On the server, as your admin user:

```bash
sudo install -m 755 -o root -g root /opt/alertme/app/deploy/deploy-alertme.sh /usr/local/bin/deploy-alertme.sh
echo 'command="/usr/local/bin/deploy-alertme.sh",restrict ssh-ed25519 AAAA...ci key... github-actions' \
  | sudo tee /home/deploy/.ssh/authorized_keys
sudo chown deploy:deploy /home/deploy/.ssh/authorized_keys && sudo chmod 600 /home/deploy/.ssh/authorized_keys
```

`command=...,restrict` pins that key to the deploy script and blocks shells, port forwarding and
PTYs. If the GitHub secret ever leaks, the only thing it can do is redeploy `main`.

In GitHub, go to **Settings → Secrets → Actions** and set these values:

- `CONTABO_HOST`: the new VPS IP
- `CONTABO_USER`: `deploy`
- `CONTABO_SSH_KEY`: the full contents of `$HOME\.ssh\alertme-ci` (the private key)

`.github/workflows/deploy.yml` needs no changes. If you later edit `deploy-alertme.sh`, run the
`install` line again, because the server runs the copy in `/usr/local/bin`.

## 9. Nightly DB backups

```bash
sudo install -m 700 -o root -g root /opt/alertme/app/deploy/backup-mysql.sh /usr/local/bin/backup-alertme.sh
sudo /usr/local/bin/backup-alertme.sh && sudo ls -lh /var/backups/alertme
echo '30 2 * * * root /usr/local/bin/backup-alertme.sh' | sudo tee /etc/cron.d/alertme-backup
```

Backups that stay on the VPS are lost with the VPS. Pull them to your laptop now and then (with `scp`),
or ship them to S3/R2.

## 10. Point the clients at the new API

- `insight-dashboard`: set `VITE_API_BASE_URL=https://api.<your-domain>`, then rebuild and redeploy.
  Vite bakes this value in at build time.
- `alertme-mobile`: update its API base URL.

---

## Check that it's locked down

On the server:

```bash
sudo ss -tlnp            # only :22 and 127.0.0.1:3306 should be listening
sudo ufw status verbose  # 22/tcp LIMIT, everything else denied
sudo fail2ban-client status sshd
docker compose -f /opt/alertme/app/deploy/docker-compose.yml ps
```

From your laptop:

```powershell
Test-NetConnection <VPS-IP> -Port 3306    # TcpTestSucceeded : False
Test-NetConnection <VPS-IP> -Port 8080    # False
ssh root@<VPS-IP>                         # Permission denied (publickey)
```

## Day-to-day

```bash
cd /opt/alertme/app/deploy
docker compose logs -f api            # live logs
docker compose restart api
docker compose pull cloudflared && docker compose up -d   # update the tunnel client
```

App files (`Logs/`, `PaperTradingState.json`, Data Protection keys) live in the `api-data` volume.
MySQL data lives in `mysql-data`. Both survive redeploys.

## Adding Ollama later

Add an `ollama` service on the `db` network with **no `ports:`** and set
`Ollama__BaseUrl=http://ollama:11434` in `api.env`. Ollama has no authentication, so a published
11434 port is an open door. Mistral 7B needs about 6 GB of free RAM.

## Optional next step: close port 22 too

You can put SSH behind the same tunnel with Cloudflare Access (`cloudflared access ssh`). After that,
UFW can deny everything inbound. GitHub Actions would then need a Cloudflare service token to reach
the box, so do this once everything else is stable.
