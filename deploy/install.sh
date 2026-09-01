#!/usr/bin/env bash
set -euo pipefail

script_dir=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)
# shellcheck source=deploy/release-lib.sh
source "$script_dir/release-lib.sh"

for command_name in docker gzip jq openssl sed sha256sum stat tar uname; do
  require_command "$command_name"
done
require_release_host
docker compose version >/dev/null 2>&1 || {
  echo "Docker Compose v2 is required." >&2
  exit 1
}

install_dir=${SUB2API_REPORT_INSTALL_DIR:-/opt/sub2api-report}
start_services=${SUB2API_REPORT_START:-true}
[[ $start_services == true || $start_services == false ]] || {
  echo "SUB2API_REPORT_START must be true or false." >&2
  exit 2
}
if [[ $(id -u) -ne 0 ]]; then
  echo "Run this installer as root (for example: sudo ./install.sh)." >&2
  exit 2
fi
if [[ -e $install_dir/compose.yaml || -e $install_dir/.env ]]; then
  echo "An installation already exists in $install_dir; run bootstrap.sh to update it." >&2
  exit 1
fi

verify_release_bundle "$script_dir"
load_release_images "$script_dir"
validate_loaded_images "$script_dir"
activate_loaded_images "$script_dir"
install_release_files "$script_dir" "$install_dir"
write_instance_env "$install_dir"
docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" config --quiet
if [[ $start_services == false ]]; then
  version=$(jq -r '.version' "$install_dir/release-manifest.json")
  echo "Sub2API Report $version is prepared in $install_dir."
  echo "Start it with: cd $install_dir && sudo docker compose up -d"
  exit 0
fi

echo "Starting Sub2API Report..."
docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" up -d app
if ! wait_for_service_health "$install_dir" app 180; then
  docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" logs --no-color app >&2 || true
  docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" down --remove-orphans || true
  echo "Sub2API Report failed to become healthy; the installation was stopped." >&2
  exit 1
fi

app_port=$(sed -n 's/^APP_PORT=//p' "$install_dir/.env" | head -n 1)
echo "Sub2API Report is healthy at http://localhost:${app_port}"
echo "Manage it with $install_dir/appctl."
