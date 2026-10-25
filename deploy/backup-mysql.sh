#!/usr/bin/env bash
# Nightly MySQL dump. Installed as /usr/local/bin/backup-alertme.sh and run from root's crontab.
# Backups on the same VPS don't survive losing the VPS - copy them off-server too.
set -euo pipefail

BACKUP_DIR=/var/backups/alertme
KEEP_DAYS=7

mkdir -p "$BACKUP_DIR"
chmod 700 "$BACKUP_DIR"

cd /opt/alertme/app/deploy
# Credentials are read inside the container from its own env, so they never appear in this
# host's process list. pipefail makes a failed dump fail the script instead of saving an empty gz.
docker compose exec -T mysql sh -c \
  'exec mysqldump --single-transaction --routines --triggers -uroot -p"$MYSQL_ROOT_PASSWORD" "$MYSQL_DATABASE"' \
  | gzip > "$BACKUP_DIR/alertme-$(date +%F-%H%M).sql.gz"

find "$BACKUP_DIR" -name 'alertme-*.sql.gz' -mtime +"$KEEP_DAYS" -delete
