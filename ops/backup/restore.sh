#!/usr/bin/env bash
#
# QuickShare — restore a PostgreSQL backup from R2 (age-encrypted).
# Used by the quarterly restore drill (`.github/workflows/restore-drill.yml`)
# and for manual incident recovery. See docs/backup-restore-runbook.md.
#
# Required environment:
#   RESTORE_DB_CONNECTION_STRING   target DB to restore INTO (e.g. staging)
#   BACKUP_AGE_PRIVATE_KEY         age private key (age1secret...) for decryption
#   BACKUP_R2_ACCOUNT_ID           Cloudflare R2 account id
#   BACKUP_R2_ACCESS_KEY_ID        R2 API token access key id
#   BACKUP_R2_ACCESS_KEY_SECRET    R2 API token secret
#   BACKUP_R2_BUCKET               R2 bucket name (versioning enabled)
#
# Optional environment:
#   BACKUP_FILE                    specific object key; empty = restore latest
#   BACKUP_PATH_PREFIX             object prefix inside the bucket (default quickshare/db)
#   RESTORE_VERIFY_QUERY           SQL run against the restored DB; non-zero rows = fail
#
set -euo pipefail

DB_CONNECTION_STRING="${RESTORE_DB_CONNECTION_STRING:?RESTORE_DB_CONNECTION_STRING is required}"
AGE_PRIVATE_KEY="${BACKUP_AGE_PRIVATE_KEY:?BACKUP_AGE_PRIVATE_KEY is required}"
R2_ACCOUNT_ID="${BACKUP_R2_ACCOUNT_ID:?BACKUP_R2_ACCOUNT_ID is required}"
R2_ACCESS_KEY_ID="${BACKUP_R2_ACCESS_KEY_ID:?BACKUP_R2_ACCESS_KEY_ID is required}"
R2_ACCESS_KEY_SECRET="${BACKUP_R2_ACCESS_KEY_SECRET:?BACKUP_R2_ACCESS_KEY_SECRET is required}"
R2_BUCKET="${BACKUP_R2_BUCKET:?BACKUP_R2_BUCKET is required}"

PATH_PREFIX="${BACKUP_PATH_PREFIX:-quickshare/db}"
TARGET_FILE="${BACKUP_FILE:-}"

TMP_DIR="$(mktemp -d)"
IDENTITY_FILE="${TMP_DIR}/age-key.txt"
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

# ── 1. Pick the backup object ───────────────────────────────────────────────
if [ -z "${TARGET_FILE}" ]; then
  log "No BACKUP_FILE specified — selecting latest"
  TARGET_FILE="$(rclone lsf "${REMOTE}" --files-only | sort | tail -n 1)"
  if [ -z "${TARGET_FILE}" ]; then
    log "ERROR: no backups found at ${REMOTE}" >&2
    exit 1
  fi
fi
log "Restoring ${TARGET_FILE}"

# ── 2. Download the encrypted dump ──────────────────────────────────────────
ENCRYPTED="${TMP_DIR}/$(basename "${TARGET_FILE}")"
rclone copyto "${REMOTE}/${TARGET_FILE}" "${ENCRYPTED}" --s3-no-check-bucket

# ── 3. Decrypt with age (identity via temp file, 600 perms) ─────────────────
printf '%s\n' "${AGE_PRIVATE_KEY}" > "${IDENTITY_FILE}"
chmod 600 "${IDENTITY_FILE}"

DUMP="${TMP_DIR}/dump.custom"
age --decrypt --identity "${IDENTITY_FILE}" --output "${DUMP}" "${ENCRYPTED}"

if [ ! -s "${DUMP}" ]; then
  log "ERROR: decrypted dump is empty — aborting" >&2
  exit 1
fi

# ── 4. Restore into the target DB ───────────────────────────────────────────
log "Restoring into target database"
pg_restore --dbname="${DB_CONNECTION_STRING}" \
  --clean --if-exists --no-owner --no-privileges "${DUMP}"

# ── 5. Optional verification query (restore-drill gate) ─────────────────────
if [ -n "${RESTORE_VERIFY_QUERY:-}" ]; then
  log "Running verification query"
  ROWS="$(psql "${DB_CONNECTION_STRING}" -tA -c "${RESTORE_VERIFY_QUERY}")"
  if [ -z "${ROWS}" ] || [ "${ROWS}" = "0" ]; then
    log "ERROR: verification query returned no rows — restore failed" >&2
    exit 1
  fi
  log "Verification OK: ${ROWS} row(s)"
fi

log "Restore complete: ${TARGET_FILE}"
