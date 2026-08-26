#!/usr/bin/env sh
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$script_dir"

case "$(uname -m)" in
  x86_64|amd64) ;;
  *)
    printf '%s\n' "Sub2API Report currently supports linux/amd64 only." >&2
    exit 1
    ;;
esac

command -v docker >/dev/null 2>&1 || {
  printf '%s\n' "Docker Engine is required." >&2
  exit 1
}
docker compose version >/dev/null 2>&1 || {
  printf '%s\n' "Docker Compose v2 is required." >&2
  exit 1
}

if [ ! -f .env ]; then
  cp .env.example .env
fi

mkdir -p secrets
if [ ! -f secrets/updater-token ]; then
  umask 077
  od -An -N32 -tx1 /dev/urandom | tr -d ' \n' > secrets/updater-token
fi
chmod 600 secrets/updater-token

docker compose --env-file .env -f compose.yaml config --quiet
docker compose --env-file .env -f compose.yaml up --detach --build

printf '%s\n' "Sub2API Report containers started."
printf '%s\n' "Read startup status with: docker compose --env-file .env -f compose.yaml logs app"
