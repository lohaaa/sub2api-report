#!/usr/bin/env bash
set -euo pipefail

script_dir=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)
cd "$script_dir"

for command_name in docker openssl; do
  command -v "$command_name" >/dev/null 2>&1 || {
    echo "$command_name is required." >&2
    exit 2
  }
done
docker compose version >/dev/null

if [[ ! -f .env ]]; then
  grep -v '^INSTANCE_ID=' .env.example > .env
  printf 'INSTANCE_ID=%s\n' "$(openssl rand -hex 16)" >> .env
elif ! grep -Eq '^INSTANCE_ID=.+$' .env; then
  grep -v '^INSTANCE_ID=' .env > .env.tmp
  printf 'INSTANCE_ID=%s\n' "$(openssl rand -hex 16)" >> .env.tmp
  mv .env.tmp .env
fi
chmod 0600 .env

install -d -m 0700 secrets
if [[ ! -s secrets/updater-token ]]; then
  openssl rand -hex 32 > secrets/updater-token
fi
chmod 0444 secrets/updater-token

docker compose -f compose.yaml -f compose.dev.yaml config --quiet
docker compose -f compose.yaml -f compose.dev.yaml up --detach --build

echo "Development containers are running at http://127.0.0.1:$(grep '^APP_PORT=' .env | cut -d= -f2)."
