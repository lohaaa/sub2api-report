#!/usr/bin/env bash
set -euo pipefail

script_dir=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)
cd "$script_dir"

command -v docker >/dev/null 2>&1 || {
  echo "docker is required." >&2
  exit 2
}
docker compose version >/dev/null

# shellcheck source=deploy/release-lib.sh
source "$script_dir/release-lib.sh"
write_instance_env "$script_dir"

docker compose -f compose.yaml -f compose.dev.yaml config --quiet
docker compose -f compose.yaml -f compose.dev.yaml up --detach --build

echo "Development containers are running at http://127.0.0.1:$(grep '^APP_PORT=' .env | cut -d= -f2)."
