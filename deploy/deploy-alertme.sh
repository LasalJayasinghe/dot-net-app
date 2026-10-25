#!/usr/bin/env bash
# Installed on the server as /usr/local/bin/deploy-alertme.sh and run by GitHub Actions
# (.github/workflows/deploy.yml). The CI key is pinned to this script in authorized_keys,
# so a leaked GitHub secret can trigger a redeploy and nothing else.
set -euo pipefail

APP_DIR=/opt/alertme/app

cd "$APP_DIR"
git fetch --quiet origin main
# Untracked deploy/.env and deploy/api.env are gitignored, so reset leaves them alone
git reset --hard --quiet origin/main

cd "$APP_DIR/deploy"
docker compose up -d --build --remove-orphans
docker image prune -f >/dev/null

docker compose ps
