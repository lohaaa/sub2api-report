#!/usr/bin/env bash
set -euo pipefail

repo_root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
test_root=$(mktemp -d /tmp/sub2api-report-documentation-test.XXXXXX)
trap 'rm -rf "$test_root"' EXIT
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
grep -Fq 'SUB2API_REPORT_PORT=18080 bash' "$readme"
grep -Fq 'SUB2API_REPORT_PORT=18080 bash' "$server_docs"
grep -Fq 'APP_PORT=8081' "$repo_root/deploy/.env.example"
grep -Fq "\${APP_PORT:-8081}:8080" "$repo_root/deploy/compose.yaml"
grep -Fq 'SUB2API_REPORT_START=false bash' "$readme"
grep -Fq 'SUB2API_REPORT_START=false bash' "$docker_docs"
if grep -Eq 'sudo +SUB2API_REPORT_(START|VERSION)' "$readme" "$docker_docs"; then
  echo "Docker bootstrap configuration must run in the unprivileged shell." >&2
  exit 1
fi

if grep -Fq 'docker compose logs' "$server_docs"; then
  echo "Server deployment documentation must not use Docker log commands." >&2
  exit 1
fi
if grep -Fq 'journalctl -u sub2api-report' "$docker_docs"; then
  echo "Docker deployment documentation must not use systemd log commands." >&2
  exit 1
fi

version=$(sed -n 's:.*<VersionPrefix>\(.*\)</VersionPrefix>.*:\1:p' "$repo_root/Directory.Build.props")
[[ $(jq -r '.version' "$repo_root/package.json") == "$version" ]]
[[ $(jq -r '.version' "$repo_root/web/package.json") == "$version" ]]
grep -Fq "VERSION: ${version}-dev" "$repo_root/deploy/compose.dev.yaml"
"$repo_root/deploy/extract-release-notes.sh" \
  "$repo_root/CHANGELOG.md" "$version" "$test_root/release-notes.md"
grep -Fq "manual_upgrade_required=\${MANUAL_UPGRADE_REQUIRED:-true}" \
  "$repo_root/deploy/build-release-assets.sh"
grep -Fq "online_install_supported=\${ONLINE_INSTALL_SUPPORTED:-false}" \
  "$repo_root/deploy/build-release-assets.sh"
grep -Fq "minimum_updater_version=\${MINIMUM_UPDATER_VERSION:-\$version}" \
  "$repo_root/deploy/build-release-assets.sh"

policy_error="$test_root/release-policy-error.log"
if MANUAL_UPGRADE_REQUIRED=true ONLINE_INSTALL_SUPPORTED=true \
  "$repo_root/deploy/build-release-assets.sh" "$version" "$test_root/invalid-release" \
  > /dev/null 2>"$policy_error"; then
  echo "Manual-only release policy unexpectedly allowed online installation." >&2
  exit 1
fi
grep -Fq 'Manual-only releases cannot advertise online installation support.' "$policy_error"

echo "deployment documentation tests passed"
