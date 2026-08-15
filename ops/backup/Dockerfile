# ── QuickShare backup/restore tool image ──
# Tiny Alpine image bundling pg_dump/pg_restore + rclone + age, wrapping the
# backup.sh / restore.sh scripts. Run on a schedule via GitHub Actions or a
# cron container. See docs/backup-restore-runbook.md.
FROM alpine:3.20

# postgresql16-client: pg_dump + pg_restore + psql
# rclone: R2 (S3-compatible) upload/download
# age: stream encryption (no plaintext dumps)
RUN apk add --no-cache bash postgresql16-client rclone age coreutils

COPY backup.sh /usr/local/bin/backup
COPY restore.sh /usr/local/bin/restore
RUN chmod +x /usr/local/bin/backup /usr/local/bin/restore

# No ENTRYPOINT: `docker run <image> backup` resolves /usr/local/bin/backup via
# the container PATH and executes it through its `#!/usr/bin/env bash` shebang.
CMD ["backup"]
