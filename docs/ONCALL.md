# On-Call / Incident Response — QuickShare

Quick reference for when something breaks. For support + on-call.
Pairs with: [`observability-setup.md`](observability-setup.md) (telemetry),
[`backup-restore-runbook.md`](backup-restore-runbook.md) (data recovery),
[`deployment-dev.md`](deployment-dev.md) (deploy flow), [`SETUP.md`](SETUP.md) (run).

## 0. Know these first

| Thing | Value |
|---|---|
| Public URL | `https://quickshare.codemagic.io.vn` |
| Health (on server) | `http://127.0.0.1:8080/health` |
| App container | `quickshare`, `docker compose` at `C:\deploy\quickshare` |
| Alerting | Grafana Alerting → Telegram (`.env`: `TELEGRAM_BOT_TOKEN`, `TELEGRAM_CHAT_ID`) |
| Telemetry UI | Grafana :3000 (logs=Loki, traces=Tempo, metrics=Prometheus) |

## 1. Triage — is it actually down?

Run in order, ~2 min:

```powershell
# 1. App process health (on the server)
curl.exe -f http://127.0.0.1:8080/health

# 2. Container state
docker ps --filter name=quickshare
docker compose -f C:\deploy\quickshare\docker-compose.yml logs --tail 100 quickshare

# 3. Public URL through the tunnel
#    browser → https://quickshare.codemagic.io.vn (timeout/502 = tunnel or app)

# 4. Last deploy status
#    GitHub → Actions → latest run on dev
```

`/health` 200 + container `Up` + browser loads → not a full outage; go to §2 alerts.
Otherwise → §3 playbooks.

## 2. Alerts and what they mean

| Alert (Prometheus) | Means | First move |
|---|---|---|
| `QuickShareDown` (`up==0`, 1m) | app unreachable | §3.1 |
| `HighErrorRate` (5xx > 5%, 5m) | error spike | §3.2 |
| `HighLatency` (p95 > 2s, 5m) | slow path | §3.2, check DB + R2 |

No alert yet but a user reports an issue? → §3 anyway; every incident should end with a
rule/alert added (§6).

## 3. Playbooks

### 3.1 App down / crash-loop / not responding

```powershell
docker compose -f C:\deploy\quickshare\docker-compose.yml logs --tail 200 quickshare
# startup crash: missing DB_CONNECTION_STRING, bad config, failed migration
docker compose -f C:\deploy\quickshare\docker-compose.yml up -d --remove-orphans quickshare
```

- **Crash at startup** → almost always `DB_CONNECTION_STRING` missing/empty (or a bad
  migration). Check `.env`, check logs for the exception.
- **Crash-loop after deploy** → §4 rollback.
- **Container stopped** → restart above; investigate why it stopped (`docker inspect quickshare`).

### 3.2 High 5xx / error spike / slow

1. **Was it just deployed?** Check `GIT_SHA` (`release` property on every log line) vs the
   last deploy. New deploy + error spike = likely the deploy → §4 rollback.
2. **Find the failing path** → Loki (logs) filtered by `release` + 5xx; Tempo (traces) for the
   slow/crashing span. Grafana dashboard shows which route/status-code is climbing.
3. **DB healthy?** `/health` includes a DB check (Npgsql health check). DB down → 500s.
4. **R2 reachable?** uploads/downloads 502 → R2 creds or bucket.

### 3.3 Data loss / bad migration / DB corruption

Do **not** improvise schema fixes. Restore:

```bash
# restore latest dump into staging first, verify, then replay to prod
# see backup-restore-runbook.md §6
./ops/backup/restore.sh
```

Recovery table is in `backup-restore-runbook.md` §11:
bad migration → restore+diff schema · accidental delete → R2 version restore · total DB loss →
provision fresh + `restore.sh` + re-point app.

### 3.4 R2 object deleted / overwritten (user file gone)

R2 versioning is on → deletes are soft. Restore previous version (`backup-restore-runbook.md` §6.2):

```bash
aws s3api list-object-versions --bucket "$R2_BUCKET_NAME" --prefix "uploads/<...>" \
  --endpoint-url "https://$R2_ACCOUNT_ID.r2.cloudflarestorage.com"
# copy the previous versionId back to the live key
```

### 3.5 Login broken

Almost always Google OAuth config: `Authentication__Google__ClientId` /
`ClientSecret` missing or wrong. Check `.env` on the server. Dev auto-signin (local only) is
not present in the container — prod needs real OAuth.

### 3.6 P2P "Direct connection failed" — NOT an incident

P2P is ICE-direct only (STUN, no TURN). Two devices on strict NAT (different 4G carriers)
**always** fail by design. The R2 server path is the fallback. Do not page anyone; add a TURN
server when it becomes a funded feature (see `deployment-dev.md` §Notes).

### 3.7 Email not sending

Email is optional (blank `EMAIL_*` = disabled). Brevo free tier caps ~300/day. Check
`Email__Host/Port/User/Password/From` in `.env`; sender domain must be verified in Brevo.

## 4. Rollback a bad deploy

`GITHUB_TOKEN` cannot pull ghcr user packages, so rollback = rebuild the last-good commit
locally on the runner (same as the deploy job):

```powershell
# on the server (runner workspace holds the checkout)
cd <runner workspace>
git checkout <last-good-sha>
docker build -t ghcr.io/chicuong2k3/quickshare:dev .
cd C:\deploy\quickshare
docker compose up -d --remove-orphans quickshare
# verify:
curl.exe -f http://127.0.0.1:8080/health
```

The image tag `:<sha>` on ghcr exists for every deploy, but use the local rebuild path
(no PAT pull needed).

## 5. Where everything lives (telemetry map)

| Signal | Where | How to reach |
|---|---|---|
| Logs | Loki | Grafana → Explore → Loki |
| Traces | Tempo | Grafana → Explore → Tempo (via Alloy :4318) |
| Metrics | Prometheus | Grafana dashboards; raw `http://<server>:9001/metrics` |
| Alerts | Grafana Alerting | → Telegram |
| Release marker | `GIT_SHA` env | `release` property on every log line + trace |

PII is redacted before Loki (`PiiRedactingEnricher`) — email/file-name/IP never reach logs.

## 6. Post-incident

1. Write a 3-line note in the incident: symptom → root cause → fix.
2. If no alert caught it, **add one** (Prometheus rule or a health check) — no alert = the
   outage repeats silently.
3. If a new failure mode, add a playbook entry above + update `docs/SETUP.md` troubleshooting.
4. If data was touched, confirm the restore drill still passes (`backup-restore-runbook.md` §7).

## Keep fresh

Update when a new alert, service, or failure mode appears. See `AGENTS.md` → Issue Completion
Docs Update Rule.

## Personal-data breach — Vietnam (Decree 13/2023, issue #230)

A different clock and a different recipient from the GDPR path above, so it runs alongside it
rather than inside it.

1. **Clock starts when we become aware**, not when we finish investigating. 72 hours to notify
   the Vietnamese authority (A08) and the affected people.
2. **Decide scope first**: whose personal data, what categories, whether it left our storage.
   The delivery and security-event tables are the record; `docs/compliance-architecture.md` has
   the data map.
3. **Notify A08** with what is known so far. An incomplete notification inside the window beats a
   complete one outside it.
4. **Notify affected people** in Vietnamese, plainly: what happened, what data, what to do.
5. **Record it** — the incident, the times, who decided what. That record is the evidence the
   deadline was met.

Owner: whoever holds the on-call pager at the time; escalate to the account owner immediately,
because the notification is a legal act rather than an engineering one.
