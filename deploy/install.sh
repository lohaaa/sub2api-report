#!/usr/bin/env bash
set -euo pipefail

bundle_dir=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)
install_dir=${SUB2API_REPORT_INSTALL_DIR:-/opt/sub2api-report}

if [[ $(id -u) -ne 0 ]]; then
  echo "Run this installer as root (for example: sudo ./install.sh)." >&2
  exit 2
fi

# shellcheck source=deploy/release-lib.sh
source "$bundle_dir/release-lib.sh"
for command_name in docker gzip jq openssl sha256sum stat; do
  require_command "$command_name"
done
require_release_host
docker compose version >/dev/null

if [[ -e $install_dir/compose.yaml ]]; then
  echo "$install_dir is already installed; use update.sh for a release update." >&2
  exit 1
fi

verify_release_bundle "$bundle_dir"
load_release_images "$bundle_dir"
validate_loaded_images "$bundle_dir"
activate_loaded_images "$bundle_dir"

install_release_files "$bundle_dir" "$install_dir"
install -m 0755 "$bundle_dir/release-lib.sh" "$install_dir/release-lib.sh"
install -m 0755 "$bundle_dir/update.sh" "$install_dir/update.sh"
write_instance_env "$install_dir"
write_updater_token "$install_dir"

docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" config --quiet
docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" up --detach --no-build

if ! wait_for_service_health "$install_dir" app 120; then
  docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" logs --tail 100 app >&2 || true
  docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" down || true
  echo "App did not become healthy. Inspect the logs in $install_dir." >&2
  exit 1
fi
if ! wait_for_service_health "$install_dir" updater 60; then
  docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" logs --tail 100 updater >&2 || true
  docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" down || true
  echo "Updater did not become healthy. Inspect the logs in $install_dir." >&2
  exit 1
fi

version=$(jq -r '.version' "$install_dir/release-manifest.json")
echo "Sub2API Report $version is running on port $(grep '^APP_PORT=' "$install_dir/.env" | cut -d= -f2)."
echo "Installation directory: $install_dir"
echo "Read the one-time initialization code with: cd $install_dir && docker compose logs app"
