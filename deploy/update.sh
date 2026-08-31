#!/usr/bin/env bash
set -euo pipefail

bundle_dir=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)
install_dir=${SUB2API_REPORT_INSTALL_DIR:-/opt/sub2api-report}
start_services=${SUB2API_REPORT_START:-true}
app_health_timeout=${SUB2API_REPORT_APP_HEALTH_TIMEOUT:-120}
updater_health_timeout=${SUB2API_REPORT_UPDATER_HEALTH_TIMEOUT:-60}
if [[ ! $app_health_timeout =~ ^[1-9][0-9]*$ \
  || ! $updater_health_timeout =~ ^[1-9][0-9]*$ ]]; then
  echo "Health timeouts must be positive integers." >&2
  exit 2
fi

if [[ $start_services != true && $start_services != false ]]; then
  echo "SUB2API_REPORT_START must be true or false." >&2
  exit 2
fi
if [[ $(id -u) -ne 0 ]]; then
  echo "Run this updater as root (for example: sudo ./update.sh)." >&2
  exit 2
fi

# shellcheck source=deploy/release-lib.sh
source "$bundle_dir/release-lib.sh"
for command_name in docker gzip jq openssl sha256sum sort stat; do
  require_command "$command_name"
done
require_release_host
docker compose version >/dev/null

install_control_files() {
  install_release_files "$bundle_dir" "$install_dir"
  install -m 0755 "$bundle_dir/release-lib.sh" "$install_dir/release-lib.sh"
  install -m 0755 "$bundle_dir/update.sh" "$install_dir/update.sh"
  write_instance_env "$install_dir"
  write_updater_token "$install_dir"
  docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" config --quiet
}

control_file_names=(
  compose.yaml .env .env.example upgrade-contract.json release-compatibility.json
  release-manifest.json release-manifest.sig update-public-key.pem release-lib.sh update.sh appctl
)

backup_control_files() {
  install -d -m 0700 "$config_backup"
  local file_name
  for file_name in "${control_file_names[@]}"; do
    [[ ! -f $install_dir/$file_name ]] || cp -p "$install_dir/$file_name" "$config_backup/$file_name"
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

if [[ ! -f $install_dir/compose.yaml || ! -f $install_dir/.env ]]; then
  echo "No installation was found in $install_dir; use install.sh first." >&2
  exit 1
fi

verify_release_bundle "$bundle_dir"
target_version=$(jq -r '.version' "$bundle_dir/release-manifest.json")
old_app_id=$(resolve_service_image_id "$install_dir" app)
old_updater_id=$(resolve_service_image_id "$install_dir" updater)
current_app_version=$(docker image inspect "$old_app_id" \
  --format '{{index .Config.Labels "org.opencontainers.image.version"}}')
current_updater_version=$(docker image inspect "$old_updater_id" \
  --format '{{index .Config.Labels "org.opencontainers.image.version"}}')
for current_version in "$current_app_version" "$current_updater_version"; do
  [[ $current_version =~ ^[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.-]+)?$ ]] || {
    echo "The running deployment has an invalid image version label." >&2
    exit 1
  }
done
for current_version in "$current_app_version" "$current_updater_version"; do
  oldest_version=$(printf '%s\n%s\n' "$current_version" "$target_version" | sort -V | head -n 1)
  if [[ $oldest_version != "$current_version" && ${ALLOW_RELEASE_DOWNGRADE:-0} != 1 ]]; then
    echo "Refusing to downgrade component $current_version to $target_version. Set ALLOW_RELEASE_DOWNGRADE=1 only after reviewing rollback compatibility." >&2
    exit 1
  fi
done
if [[ -f $install_dir/update-public-key.pem ]] \
  && ! cmp --silent "$install_dir/update-public-key.pem" "$bundle_dir/update-public-key.pem" \
  && [[ ${ALLOW_RELEASE_KEY_ROTATION:-0} != 1 ]]; then
  echo "The release signing key changed. Verify the key rotation notice, then rerun with ALLOW_RELEASE_KEY_ROTATION=1." >&2
  exit 1
fi
timestamp=$(date -u +'%Y%m%dT%H%M%SZ')
config_backup="$install_dir/deploy-backups/$timestamp"
backup_control_files
rollback_control_sync() {
  local exit_code=${1:-1}
  trap - ERR
  set +e
  echo "Control file synchronization failed; restoring previous files." >&2
  restore_control_files
  docker tag "$old_app_id" sub2api-report-app:current
  docker tag "$old_updater_id" sub2api-report-updater:bootstrap
  if [[ $start_services == true ]]; then
    docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" \
      up --detach --no-build
  fi
  exit "$exit_code"
}
if [[ $target_version == "$current_app_version" && $target_version == "$current_updater_version" ]]; then
  trap 'rollback_control_sync $?' ERR
  docker tag "$old_app_id" sub2api-report-app:current
  docker tag "$old_updater_id" sub2api-report-updater:bootstrap
  install_control_files
  if [[ $start_services == true ]]; then
    docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" \
      up --detach --no-build
    wait_for_service_health "$install_dir" app "$app_health_timeout"
    wait_for_service_health "$install_dir" updater "$updater_health_timeout"
  else
    docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" \
      stop app updater
  fi
  trap - ERR
  echo "Sub2API Report $target_version control files are synchronized."
  exit 0
fi

data_backup="$install_dir/data-backups/$timestamp"
install -d -m 0700 "$data_backup"

docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" stop app
if ! docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" \
  run --rm --no-deps --user 0:0 --volume "$data_backup:/host-backup" \
  --entrypoint sh app -c 'tar -C /data -cf /host-backup/db.tar db'; then
  docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" up --detach --no-build app
  echo "Database backup failed; the installed release was not changed." >&2
  exit 1
fi
if [[ ! -s $data_backup/db.tar ]] || ! (
  cd "$data_backup"
  sha256sum db.tar > checksums.txt
); then
  docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" up --detach --no-build app
  echo "Database backup validation failed; the installed release was not changed." >&2
  exit 1
fi

rollback_pending=1
rollback_release() {
  local exit_code=${1:-1}
  set +e
  echo "Release update failed; restoring the previous deployment." >&2
  docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" stop app updater
  docker tag "$old_app_id" sub2api-report-app:current
  docker tag "$old_updater_id" sub2api-report-updater:bootstrap
  restore_control_files
  if ! (cd "$data_backup" && sha256sum --check --strict checksums.txt >/dev/null); then
    echo "The pre-update database backup failed checksum validation. Database restore was not attempted." >&2
    exit "$exit_code"
  fi
  if ! docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" \
    run --rm --no-deps --user 0:0 \
    --cap-add DAC_OVERRIDE --cap-add FOWNER --cap-add CHOWN \
    --volume "$data_backup:/host-backup:ro" \
    --entrypoint sh app -c \
    'rm -rf /data/db && tar -C /data -xf /host-backup/db.tar'; then
    echo "Database restore failed and requires operator intervention." >&2
    exit "$exit_code"
  fi
  docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" up --detach --no-build --force-recreate
  if wait_for_service_health "$install_dir" app "$app_health_timeout" \
    && wait_for_service_health "$install_dir" updater "$updater_health_timeout"; then
    echo "Previous deployment restored. Backup retained at $data_backup." >&2
  else
    echo "Previous deployment was restored but did not become healthy; operator intervention is required. Backup: $data_backup" >&2
  fi
  exit "$exit_code"
}
trap '[[ $rollback_pending -eq 0 ]] || rollback_release $?' ERR

load_release_images "$bundle_dir"
validate_loaded_images "$bundle_dir"
activate_loaded_images "$bundle_dir"
install_control_files
docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" \
  up --detach --no-build --force-recreate

if ! wait_for_service_health "$install_dir" app "$app_health_timeout"; then
  rollback_release 1
fi
if ! wait_for_service_health "$install_dir" updater "$updater_health_timeout"; then
  rollback_release 1
fi

rollback_pending=0
trap - ERR
if [[ $start_services == false ]]; then
  docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" \
    stop app updater
fi
version=$(jq -r '.version' "$install_dir/release-manifest.json")
echo "Sub2API Report was updated to $version."
echo "Pre-update data backup retained at $data_backup."
