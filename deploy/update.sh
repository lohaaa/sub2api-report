#!/usr/bin/env bash
set -euo pipefail

bundle_dir=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)
install_dir=${SUB2API_REPORT_INSTALL_DIR:-/opt/sub2api-report}
start_services=${SUB2API_REPORT_START:-true}
app_health_timeout=${SUB2API_REPORT_APP_HEALTH_TIMEOUT:-180}

[[ $app_health_timeout =~ ^[1-9][0-9]*$ ]] || {
  echo "SUB2API_REPORT_APP_HEALTH_TIMEOUT must be a positive integer." >&2
  exit 2
}
[[ $start_services == true || $start_services == false ]] || {
  echo "SUB2API_REPORT_START must be true or false." >&2
  exit 2
}
if [[ $(id -u) -ne 0 ]]; then
  echo "Run this update command as root (for example: sudo ./update.sh)." >&2
  exit 2
fi

# shellcheck source=deploy/release-lib.sh
source "$bundle_dir/release-lib.sh"
for command_name in cmp docker flock gzip jq openssl sha256sum sort stat tar uname; do
  require_command "$command_name"
done
require_release_host
docker compose version >/dev/null

if [[ ! -f $install_dir/compose.yaml || ! -f $install_dir/.env ]]; then
  echo "No installation was found in $install_dir; use install.sh first." >&2
  exit 1
fi

update_lock_file="$install_dir/.update.lock"
exec 9>"$update_lock_file"
if ! flock --nonblock 9; then
  echo "Another Sub2API Report update is already running for $install_dir." >&2
  exit 1
fi

verify_release_bundle "$bundle_dir"
target_version=$(jq -r '.version' "$bundle_dir/release-manifest.json")
old_app_id=$(resolve_service_image_id "$install_dir" app)
current_app_version=$(docker image inspect "$old_app_id" \
  --format '{{index .Config.Labels "org.opencontainers.image.version"}}')
current_contract=$(docker image inspect "$old_app_id" \
  --format '{{index .Config.Labels "io.sub2api-report.contract"}}')
[[ $current_app_version =~ ^[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.-]+)?$ ]] || {
  echo "The installed App has an invalid version label." >&2
  exit 1
}
[[ $current_contract == 1 || $current_contract == 2 ]] || {
  echo "The installed App uses unsupported deployment contract '$current_contract'." >&2
  exit 1
}
oldest_version=$(printf '%s\n%s\n' "$current_app_version" "$target_version" | sort -V | head -n 1)
if [[ $oldest_version != "$current_app_version" && ${ALLOW_RELEASE_DOWNGRADE:-0} != 1 ]]; then
  echo "Refusing to downgrade $current_app_version to $target_version. Set ALLOW_RELEASE_DOWNGRADE=1 only after reviewing rollback compatibility." >&2
  exit 1
fi
if [[ -f $install_dir/update-public-key.pem ]] \
  && ! cmp --silent "$install_dir/update-public-key.pem" "$bundle_dir/update-public-key.pem" \
  && [[ ${ALLOW_RELEASE_KEY_ROTATION:-0} != 1 ]]; then
  echo "The release signing key changed. Verify the rotation notice, then rerun with ALLOW_RELEASE_KEY_ROTATION=1." >&2
  exit 1
fi

has_legacy_updater=false
old_updater_id=
old_updater_container_id=
if docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" \
  config --services | grep -qx updater; then
  has_legacy_updater=true
  old_updater_container_id=$(resolve_service_container_id "$install_dir" updater)
  old_updater_id=$(docker inspect --format '{{.Image}}' "$old_updater_container_id")
  [[ $old_updater_id =~ ^sha256:[a-f0-9]{64}$ ]] || {
    echo "The legacy Updater container has an invalid image reference." >&2
    exit 1
  }
fi

timestamp=$(date -u +'%Y%m%dT%H%M%SZ')
config_backup="$install_dir/deploy-backups/$timestamp"
data_backup="$install_dir/data-backups/$timestamp"
control_file_names=(
  compose.yaml .env .env.example upgrade-contract.json release-compatibility.json
  release-manifest.json release-manifest.sig update-public-key.pem release-lib.sh update.sh appctl
)

backup_control_files() {
  install -d -m 0700 "$config_backup"
  local file_name
  for file_name in "${control_file_names[@]}"; do
    [[ ! -f $install_dir/$file_name ]] \
      || cp -p "$install_dir/$file_name" "$config_backup/$file_name"
  done
}

restore_control_files() {
  local file_name
  for file_name in "${control_file_names[@]}"; do
    if [[ -f $config_backup/$file_name ]]; then
      cp -p "$config_backup/$file_name" "$install_dir/$file_name"
    else
      rm -f "$install_dir/$file_name"
    fi
  done
}

install_control_files() {
  install_release_files "$bundle_dir" "$install_dir"
  install -m 0755 "$bundle_dir/release-lib.sh" "$install_dir/release-lib.sh"
  install -m 0755 "$bundle_dir/update.sh" "$install_dir/update.sh"
  rm -f "$install_dir/release-compatibility.json"
  write_instance_env "$install_dir"
  docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" config --quiet
}

start_previous_deployment() {
  local -a services=(app)
  [[ $has_legacy_updater == false ]] || services+=(updater)
  docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" \
    up --detach --no-build --force-recreate "${services[@]}"
}

wait_for_previous_deployment() {
  wait_for_service_health "$install_dir" app "$app_health_timeout" || return
  if [[ $has_legacy_updater == true ]]; then
    wait_for_service_health "$install_dir" updater 120 || return
  fi
}

remove_legacy_updater() {
  for _ in 1 2 3 4 5; do
    if docker rm "$old_updater_container_id" >/dev/null; then
      return 0
    fi
    sleep 2
  done
  return 1
}

backup_control_files
install -d -m 0700 "$data_backup"

restart_previous_after_interrupt() {
  trap - INT TERM
  set +e
  start_previous_deployment
  exit 130
}
trap restart_previous_after_interrupt INT TERM

# Re-anchor a possibly polluted current tag before using the installed image
docker tag "$old_app_id" sub2api-report-app:current
services_to_stop=(app)
if [[ $has_legacy_updater == true ]]; then
  docker tag "$old_updater_id" sub2api-report-updater:bootstrap
  docker update --restart=no "$old_updater_container_id" >/dev/null
  services_to_stop+=(updater)
fi
echo "Stopping the installed deployment for a consistent SQLite backup..."
if ! docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" \
  stop "${services_to_stop[@]}"; then
  start_previous_deployment || true
  echo "Could not stop the installed deployment; the release was not changed." >&2
  exit 1
fi
if ! docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" \
  run --rm --no-deps --user 0:0 --volume "$data_backup:/host-backup" \
  --entrypoint sh app -c 'tar -C /data -cf /host-backup/db.tar db'; then
  start_previous_deployment || true
  echo "Database backup failed; the installed release was not changed." >&2
  exit 1
fi
if [[ ! -s $data_backup/db.tar ]] || ! (
  cd "$data_backup"
  sha256sum db.tar > checksums.txt
); then
  start_previous_deployment || true
  echo "Database backup validation failed; the installed release was not changed." >&2
  exit 1
fi

rollback_pending=1
rollback_release() {
  local exit_code=${1:-1}
  trap - ERR INT TERM
  set +e
  echo "Release update failed; restoring the previous deployment." >&2
  docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" stop app >/dev/null 2>&1
  docker tag "$old_app_id" sub2api-report-app:current
  if [[ $has_legacy_updater == true ]]; then
    docker tag "$old_updater_id" sub2api-report-updater:bootstrap
  fi
  restore_control_files
  if ! (cd "$data_backup" && sha256sum --check --strict checksums.txt >/dev/null); then
    echo "The pre-update database backup failed checksum validation. Database restore was not attempted." >&2
    exit "$exit_code"
  fi
  if ! docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" \
    run --rm --no-deps --user 0:0 \
    --cap-add DAC_OVERRIDE --cap-add FOWNER --cap-add CHOWN \
    --volume "$data_backup:/host-backup:ro" \
    --entrypoint sh app -c 'rm -rf /data/db && tar -C /data -xf /host-backup/db.tar'; then
    echo "Database restore failed and requires operator intervention. Backup: $data_backup" >&2
    exit "$exit_code"
  fi
  if start_previous_deployment && wait_for_previous_deployment; then
    echo "Previous deployment restored. Backup retained at $data_backup." >&2
  else
    echo "Previous deployment files and database were restored but services are not healthy. Backup: $data_backup" >&2
  fi
  exit "$exit_code"
}
trap '[[ $rollback_pending -eq 0 ]] || rollback_release $?' ERR
trap '[[ $rollback_pending -eq 0 ]] || rollback_release 130' INT TERM

load_release_images "$bundle_dir"
validate_loaded_images "$bundle_dir"
activate_loaded_images "$bundle_dir"
install_control_files

echo "Starting Sub2API Report $target_version..."
docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" \
  up --detach --no-build --force-recreate app
if ! wait_for_service_health "$install_dir" app "$app_health_timeout"; then
  rollback_release 1
fi

# From this point the migrated database may receive writes. Do not restore the
# pre-update database for failures in post-health legacy cleanup.
rollback_pending=0
trap - ERR INT TERM
if [[ $has_legacy_updater == true ]] \
  && docker container inspect "$old_updater_container_id" >/dev/null 2>&1 \
  && ! remove_legacy_updater; then
  echo "The v2 App is healthy, but the stopped legacy Updater container could not be removed." >&2
  echo "Retry manually: docker rm $old_updater_container_id" >&2
  exit 1
fi
if [[ $start_services == false ]]; then
  docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" stop app
fi

echo "Sub2API Report was updated to $target_version (deployment contract v2)."
echo "Pre-update deployment files retained at $config_backup."
echo "Pre-update data backup retained at $data_backup."
