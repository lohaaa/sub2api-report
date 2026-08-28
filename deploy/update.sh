#!/usr/bin/env bash
set -euo pipefail

bundle_dir=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)
install_dir=${SUB2API_REPORT_INSTALL_DIR:-/opt/sub2api-report}

if [[ $(id -u) -ne 0 ]]; then
  echo "Run this updater as root (for example: sudo ./update.sh)." >&2
  exit 2
fi

# shellcheck source=deploy/release-lib.sh
source "$bundle_dir/release-lib.sh"
for command_name in docker find gzip jq openssl sha256sum sort stat xargs; do
  require_command "$command_name"
done
require_release_host
docker compose version >/dev/null

if [[ ! -f $install_dir/compose.yaml || ! -f $install_dir/.env ]]; then
  echo "No installation was found in $install_dir; use install.sh first." >&2
  exit 1
fi

verify_release_bundle "$bundle_dir"
installed_version=$(jq -r '.version' "$install_dir/release-manifest.json")
target_version=$(jq -r '.version' "$bundle_dir/release-manifest.json")
if [[ $target_version == "$installed_version" ]]; then
  echo "Version $target_version is already installed." >&2
  exit 1
fi
oldest_version=$(printf '%s\n%s\n' "$installed_version" "$target_version" | sort -V | head -n 1)
if [[ $oldest_version != "$installed_version" && ${ALLOW_RELEASE_DOWNGRADE:-0} != 1 ]]; then
  echo "Refusing to downgrade from $installed_version to $target_version. Set ALLOW_RELEASE_DOWNGRADE=1 only after reviewing rollback compatibility." >&2
  exit 1
fi
if [[ -f $install_dir/update-public-key.pem ]] \
  && ! cmp --silent "$install_dir/update-public-key.pem" "$bundle_dir/update-public-key.pem" \
  && [[ ${ALLOW_RELEASE_KEY_ROTATION:-0} != 1 ]]; then
  echo "The release signing key changed. Verify the key rotation notice, then rerun with ALLOW_RELEASE_KEY_ROTATION=1." >&2
  exit 1
fi

old_app_id=$(docker image inspect sub2api-report-app:current --format '{{.Id}}')
old_updater_id=$(docker image inspect sub2api-report-updater:bootstrap --format '{{.Id}}')
timestamp=$(date -u +'%Y%m%dT%H%M%SZ')
config_backup="$install_dir/deploy-backups/$timestamp"
data_backup="$install_dir/data-backups/$timestamp"
install -d -m 0700 "$config_backup" "$data_backup"
for file_name in compose.yaml .env.example upgrade-contract.json release-manifest.json \
  release-manifest.sig update-public-key.pem release-lib.sh update.sh appctl; do
  [[ ! -f $install_dir/$file_name ]] || cp -p "$install_dir/$file_name" "$config_backup/$file_name"
done

docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" stop app
if ! docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" \
  run --rm --no-deps --user 0:0 --volume "$data_backup:/host-backup" \
  --entrypoint sh app -c 'cp -a /data/db /host-backup/db'; then
  docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" up --detach --no-build app
  echo "Database backup failed; the installed release was not changed." >&2
  exit 1
fi
if [[ ! -s $data_backup/db/sub2api-report.db ]] || ! (
  cd "$data_backup"
  find db -type f -print0 | sort -z | xargs -0 sha256sum > checksums.txt
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
  for backup_file in "$config_backup"/*; do
    [[ ! -f $backup_file ]] || cp -p "$backup_file" "$install_dir/$(basename "$backup_file")"
  done
  if ! (cd "$data_backup" && sha256sum --check --strict checksums.txt >/dev/null); then
    echo "The pre-update database backup failed checksum validation. Database restore was not attempted." >&2
    exit "$exit_code"
  fi
  if ! docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" \
    run --rm --no-deps --user 0:0 --volume "$data_backup:/host-backup:ro" \
    --entrypoint sh app -c \
    'rm -rf /data/db && cp -a /host-backup/db /data/db'; then
    echo "Database restore failed and requires operator intervention." >&2
    exit "$exit_code"
  fi
  docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" up --detach --no-build --force-recreate
  if wait_for_service_health "$install_dir" app 120 \
    && wait_for_service_health "$install_dir" updater 60; then
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
install_release_files "$bundle_dir" "$install_dir"
install -m 0755 "$bundle_dir/release-lib.sh" "$install_dir/release-lib.sh"
install -m 0755 "$bundle_dir/update.sh" "$install_dir/update.sh"
write_instance_env "$install_dir"
write_updater_token "$install_dir"

docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" config --quiet
docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" \
  up --detach --no-build --force-recreate

if ! wait_for_service_health "$install_dir" app 120; then
  rollback_release 1
fi
if ! wait_for_service_health "$install_dir" updater 60; then
  rollback_release 1
fi

rollback_pending=0
trap - ERR
version=$(jq -r '.version' "$install_dir/release-manifest.json")
echo "Sub2API Report was updated to $version."
echo "Pre-update data backup retained at $data_backup."
