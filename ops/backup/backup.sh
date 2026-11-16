#!/usr/bin/env bash
#
# QuickShare — PostgreSQL backup → Cloudflare R2 (age-encrypted, versioned, 30-day retention).
#
# Plaintext never touches disk: `pg_dump` pipes straight into `age`, and the
# ciphertext is the only artifact written. Upload goes to a PRIVATE R2 bucket
# with object versioning enabled (see docs/backup-restore-runbook.md).
#
# Runs on a schedule (VPS cron or GitHub Actions `.github/workflows/backup.yml`).
#
# Required environment:
#   DB_CONNECTION_STRING          PostgreSQL connection string (libpq URI form)
#   BACKUP_AGE_PUBLIC_KEY         age recipient public key (age1...)
#   BACKUP_R2_ACCOUNT_ID          Cloudflare R2 account id
#   BACKUP_R2_ACCESS_KEY_ID       R2 API token access key id (private backup bucket)
#   BACKUP_R2_ACCESS_KEY_SECRET   R2 API token secret
#   BACKUP_R2_BUCKET              R2 bucket name (versioning enabled)
#
# Optional environment:
#   BACKUP_RETENTION_DAYS         prune dumps older than this (default 30)
#   BACKUP_PATH_PREFIX            object prefix inside the bucket (default quickshare/db)
#   BACKUP_KEEP_LOCAL             "1" to also keep the encrypted dump under /var/backups/quickshare
#
set -euo pipefail

# ── Config ──────────────────────────────────────────────────────────────────
DB_CONNECTION_STRING="${DB_CONNECTION_STRING:?DB_CONNECTION_STRING is required}"
AGE_PUBLIC_KEY="${BACKUP_AGE_PUBLIC_KEY:?BACKUP_AGE_PUBLIC_KEY is required}"
R2_ACCOUNT_ID="${BACKUP_R2_ACCOUNT_ID:?BACKUP_R2_ACCOUNT_ID is required}"
R2_ACCESS_KEY_ID="${BACKUP_R2_ACCESS_KEY_ID:?BACKUP_R2_ACCESS_KEY_ID is required}"
R2_ACCESS_KEY_SECRET="${BACKUP_R2_ACCESS_KEY_SECRET:?BACKUP_R2_ACCESS_KEY_SECRET is required}"
R2_BUCKET="${BACKUP_R2_BUCKET:?BACKUP_R2_BUCKET is required}"

RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-30}"
PATH_PREFIX="${BACKUP_PATH_PREFIX:-quickshare/db}"
KEEP_LOCAL="${BACKUP_KEEP_LOCAL:-0}"

TIMESTAMP="$(date -u +%Y%m%dT%H%M%SZ)"
NAME="quickshare-${TIMESTAMP}.dump.age"
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

log() { echo "[$(date -u +%Y-%m-%dT%H:%M:%SZ)] $*"; }

# ── rclone remote, configured via env (no config file → no secret on disk) ──
export RCLONE_CONFIG_BACKUP_TYPE="s3"
export RCLONE_CONFIG_BACKUP_PROVIDER="Cloudflare"
export RCLONE_CONFIG_BACKUP_ENDPOINT="https://${R2_ACCOUNT_ID}.r2.cloudflarestorage.com"
export RCLONE_CONFIG_BACKUP_ACCESS_KEY_ID="${R2_ACCESS_KEY_ID}"
export RCLONE_CONFIG_BACKUP_SECRET_ACCESS_KEY="${R2_ACCESS_KEY_SECRET}"
export RCLONE_CONFIG_BACKUP_REGION="auto"

REMOTE="backup:${R2_BUCKET}/${PATH_PREFIX}"

log "Starting backup ${NAME}"

# ── 1. Dump → encrypt (plaintext never written to disk) ─────────────────────
log "Dumping PostgreSQL and encrypting with age"
pg_dump --dbname="${DB_CONNECTION_STRING}" --format=custom \
  | age --encrypt --recipient "${AGE_PUBLIC_KEY}" --output "${TMP_DIR}/${NAME}"

# ── 2. Guard against empty ciphertext ───────────────────────────────────────
if [ ! -s "${TMP_DIR}/${NAME}" ]; then
  log "ERROR: encrypted dump is empty — aborting" >&2
  exit 1
fi

# ── 3. Upload to R2 (versioning gives delete protection) ────────────────────
log "Uploading to r2://${R2_BUCKET}/${PATH_PREFIX}/${NAME}"
rclone copyto "${TMP_DIR}/${NAME}" "${REMOTE}/${NAME}" --s3-no-check-bucket

# ── 4. Retention — prune dumps older than N days ────────────────────────────
log "Pruning backups older than ${RETENTION_DAYS} days"
rclone delete "${REMOTE}" --min-age "${RETENTION_DAYS}d"

# ── 5. Optional local keep (verification / audit) ───────────────────────────
if [ "${KEEP_LOCAL}" = "1" ]; then
  mkdir -p /var/backups/quickshare
  cp "${TMP_DIR}/${NAME}" "/var/backups/quickshare/${NAME}"
  log "Kept local copy at /var/backups/quickshare/${NAME}"
fi

log "Backup complete: ${NAME} (${RETENTION_DAYS}d retention)"
