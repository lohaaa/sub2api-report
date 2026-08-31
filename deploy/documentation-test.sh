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
grep -Fq 'SUB2API_REPORT_PORT=18080' "$readme"
grep -Fq 'SUB2API_REPORT_PORT=18080' "$server_docs"
grep -Fq 'SUB2API_REPORT_BIND_ADDRESS=127.0.0.1' "$readme"
grep -Fq 'SUB2API_REPORT_BIND_ADDRESS' "$docker_docs"
grep -Fq 'APP_PORT=8081' "$repo_root/deploy/.env.example"
grep -Fq "\${APP_PORT:-8081}:8080" "$repo_root/deploy/compose.yaml"
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
# shellcheck source=deploy/release-lib.sh
source "$repo_root/deploy/release-lib.sh"
validate_release_compatibility_file "$repo_root/deploy/release-compatibility.json" "$version"
manifest_schema=$(jq -r '.manifestSchemaVersion' "$repo_root/deploy/release-compatibility.json")
deployment_contract=$(jq -r '.deploymentContractVersion' "$repo_root/deploy/release-compatibility.json")
grep -Fq "ManifestSchemaVersion = $manifest_schema;" \
  "$repo_root/src/Sub2ApiReport.UpdateContracts/UpdateContractConstants.cs"
grep -Fq "DeploymentContractVersion = $deployment_contract;" \
  "$repo_root/src/Sub2ApiReport.UpdateContracts/UpdateContractConstants.cs"

echo "deployment documentation tests passed"
