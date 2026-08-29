#!/usr/bin/env bash
set -euo pipefail

repo_root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
readme="$repo_root/README.md"
server_docs="$repo_root/docs/server-deployment.md"
docker_docs="$repo_root/docs/deployment.md"

systemd_command='journalctl -u sub2api-report -b --no-pager -o cat'
docker_command='docker compose logs --no-log-prefix app'
setup_marker='grep -F "One-time setup code"'

grep -Fq "$systemd_command" "$readme"
grep -Fq "$systemd_command" "$server_docs"
grep -Fq "$docker_command" "$readme"
grep -Fq "$docker_command" "$docker_docs"
grep -Fq "$setup_marker" "$readme"
grep -Fq "$setup_marker" "$server_docs"
grep -Fq "$setup_marker" "$docker_docs"

if grep -Fq 'docker compose logs' "$server_docs"; then
  echo "Server deployment documentation must not use Docker log commands." >&2
  exit 1
fi
if grep -Fq 'journalctl -u sub2api-report' "$docker_docs"; then
  echo "Docker deployment documentation must not use systemd log commands." >&2
  exit 1
fi

echo "deployment documentation tests passed"
