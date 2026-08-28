#!/usr/bin/env bash

require_command() {
  local command_name=$1
  command -v "$command_name" >/dev/null 2>&1 || {
    echo "$command_name is required." >&2
    return 1
  }
}

require_release_host() {
  local machine
  machine=$(uname -m)
  [[ $machine == x86_64 || $machine == amd64 ]] || {
    echo "This release supports linux/amd64 only; host architecture is $machine." >&2
    return 1
  }
  [[ $(uname -s) == Linux ]] || {
    echo "This release supports Linux only." >&2
    return 1
  }
}

verify_release_bundle() {
  local bundle_dir=$1
  local app_archive="$bundle_dir/images/sub2api-report-app-linux-amd64.tar.gz"
  local updater_archive="$bundle_dir/images/sub2api-report-updater-linux-amd64.tar.gz"
  local expected_app_sha expected_updater_sha actual_app_sha actual_updater_sha
  local expected_app_size expected_updater_size actual_app_size actual_updater_size manifest_version
  local expected_notes_sha expected_notes_size actual_notes_sha actual_notes_size release_heading

  for required_file in \
    "$bundle_dir/compose.yaml" \
    "$bundle_dir/.env.example" \
    "$bundle_dir/upgrade-contract.json" \
    "$bundle_dir/CHANGELOG.md" \
    "$bundle_dir/LICENSE" \
    "$bundle_dir/RELEASE-NOTES.md" \
    "$bundle_dir/release-manifest.json" \
    "$bundle_dir/release-manifest.sig" \
    "$bundle_dir/update-public-key.pem" \
    "$bundle_dir/images/checksums.txt" \
    "$app_archive" \
    "$updater_archive"; do
    [[ -f $required_file ]] || {
      echo "Release bundle is missing ${required_file#"$bundle_dir"/}." >&2
      return 1
    }
  done

  (
    cd "$bundle_dir/images" || return
    sha256sum --check --strict checksums.txt
  )
  openssl dgst -sha256 \
    -verify "$bundle_dir/update-public-key.pem" \
    -signature "$bundle_dir/release-manifest.sig" \
    "$bundle_dir/release-manifest.json" >/dev/null

  [[ $(jq -r '.schemaVersion' "$bundle_dir/release-manifest.json") == 1 \
    && $(jq -r '.channel' "$bundle_dir/release-manifest.json") == stable ]] || {
    echo "Unsupported release manifest schema or channel." >&2
    return 1
  }
  manifest_version=$(jq -r '.version' "$bundle_dir/release-manifest.json")
  [[ $manifest_version =~ ^[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.-]+)?$ ]] || {
    echo "Release manifest version is invalid." >&2
    return 1
  }
  IFS= read -r release_heading < "$bundle_dir/RELEASE-NOTES.md"
  [[ $release_heading == "## [$manifest_version]" \
    || $release_heading == "## [$manifest_version] - "* ]] || {
    echo "Release notes do not match the signed release version." >&2
    return 1
  }

  [[ $(jq -r '.architecture' "$bundle_dir/release-manifest.json") == linux/amd64 ]] || {
    echo "Release manifest architecture is not linux/amd64." >&2
    return 1
  }
  [[ $(jq -r '.deploymentContractVersion' "$bundle_dir/release-manifest.json") == 1 ]] || {
    echo "Unsupported deployment contract." >&2
    return 1
  }
  [[ $(jq -r '.signatureAlgorithm' "$bundle_dir/release-manifest.json") == RSASSA-PKCS1-v1_5-SHA256 ]] || {
    echo "Unsupported release signature algorithm." >&2
    return 1
  }

  expected_app_sha=$(jq -r '.app.archiveSha256' "$bundle_dir/release-manifest.json")
  expected_updater_sha=$(jq -r '.updater.archiveSha256' "$bundle_dir/release-manifest.json")
  actual_app_sha=$(sha256sum "$app_archive" | awk '{print $1}')
  actual_updater_sha=$(sha256sum "$updater_archive" | awk '{print $1}')
  expected_app_size=$(jq -r '.app.size' "$bundle_dir/release-manifest.json")
  expected_updater_size=$(jq -r '.updater.size' "$bundle_dir/release-manifest.json")
  actual_app_size=$(stat -c '%s' "$app_archive")
  actual_updater_size=$(stat -c '%s' "$updater_archive")
  expected_notes_sha=$(jq -r '.releaseNotes.sha256' "$bundle_dir/release-manifest.json")
  expected_notes_size=$(jq -r '.releaseNotes.size' "$bundle_dir/release-manifest.json")
  actual_notes_sha=$(sha256sum "$bundle_dir/RELEASE-NOTES.md" | awk '{print $1}')
  actual_notes_size=$(stat -c '%s' "$bundle_dir/RELEASE-NOTES.md")
  [[ $actual_notes_sha == "$expected_notes_sha" && $actual_notes_size == "$expected_notes_size" ]] || {
    echo "Release notes do not match the signed release manifest." >&2
    return 1
  }
  [[ $actual_app_sha == "$expected_app_sha" && $actual_app_size == "$expected_app_size" ]] || {
    echo "App archive does not match the signed release manifest." >&2
    return 1
  }
  [[ $actual_updater_sha == "$expected_updater_sha" && $actual_updater_size == "$expected_updater_size" ]] || {
    echo "Updater archive does not match the signed release manifest." >&2
    return 1
  }
}

load_release_images() {
  local bundle_dir=$1
  gzip -dc "$bundle_dir/images/sub2api-report-app-linux-amd64.tar.gz" | docker load
  gzip -dc "$bundle_dir/images/sub2api-report-updater-linux-amd64.tar.gz" | docker load
}

validate_loaded_images() {
  local bundle_dir=$1
  local app_tag updater_tag app_id updater_id expected_app_id expected_updater_id
  local app_platform updater_platform app_version updater_version manifest_version

  manifest_version=$(jq -r '.version' "$bundle_dir/release-manifest.json")
  app_tag=$(jq -r '.app.loadedTag' "$bundle_dir/release-manifest.json")
  updater_tag=$(jq -r '.updater.loadedTag' "$bundle_dir/release-manifest.json")
  [[ $app_tag == "sub2api-report-app:$manifest_version" \
    && $updater_tag == "sub2api-report-updater:$manifest_version" ]] || {
    echo "Loaded image tags are outside the release allowlist." >&2
    return 1
  }

  app_id=$(docker image inspect "$app_tag" --format '{{.Id}}')
  updater_id=$(docker image inspect "$updater_tag" --format '{{.Id}}')
  expected_app_id=$(jq -r '.app.imageId' "$bundle_dir/release-manifest.json")
  expected_updater_id=$(jq -r '.updater.imageId' "$bundle_dir/release-manifest.json")
  [[ $app_id == "$expected_app_id" && $updater_id == "$expected_updater_id" ]] || {
    echo "Loaded image IDs do not match the signed release manifest." >&2
    return 1
  }

  app_platform=$(docker image inspect "$app_tag" --format '{{.Os}}/{{.Architecture}}')
  updater_platform=$(docker image inspect "$updater_tag" --format '{{.Os}}/{{.Architecture}}')
  [[ $app_platform == linux/amd64 && $updater_platform == linux/amd64 ]] || {
    echo "Loaded images are not linux/amd64." >&2
    return 1
  }

  app_version=$(docker image inspect "$app_tag" --format '{{index .Config.Labels "org.opencontainers.image.version"}}')
  updater_version=$(docker image inspect "$updater_tag" --format '{{index .Config.Labels "org.opencontainers.image.version"}}')
  [[ $app_version == "$manifest_version" && $updater_version == "$manifest_version" ]] || {
    echo "Loaded image versions do not match the signed release manifest." >&2
    return 1
  }
}

activate_loaded_images() {
  local bundle_dir=$1
  local app_tag updater_tag
  app_tag=$(jq -r '.app.loadedTag' "$bundle_dir/release-manifest.json")
  updater_tag=$(jq -r '.updater.loadedTag' "$bundle_dir/release-manifest.json")
  docker tag "$app_tag" sub2api-report-app:current
  docker tag "$updater_tag" sub2api-report-updater:bootstrap
}

write_instance_env() {
  local install_dir=$1
  local instance_id docker_gid
  [[ -S /var/run/docker.sock ]] || {
    echo "Docker Engine socket /var/run/docker.sock is required." >&2
    return 1
  }
  instance_id=
  if [[ -f $install_dir/.env ]]; then
    instance_id=$(sed -n 's/^INSTANCE_ID=//p' "$install_dir/.env" | head -n 1)
  fi
  if [[ -z $instance_id ]]; then
    instance_id=$(openssl rand -hex 16)
  fi
  docker_gid=$(stat -c '%g' /var/run/docker.sock)
  if [[ -f $install_dir/.env ]]; then
    grep -Ev '^(INSTANCE_ID|DOCKER_GID)=' "$install_dir/.env" > "$install_dir/.env.tmp"
  else
    grep -Ev '^(INSTANCE_ID|DOCKER_GID)=' "$install_dir/.env.example" > "$install_dir/.env.tmp"
  fi
  printf 'INSTANCE_ID=%s\nDOCKER_GID=%s\n' "$instance_id" "$docker_gid" >> "$install_dir/.env.tmp"
  chmod 0600 "$install_dir/.env.tmp"
  mv "$install_dir/.env.tmp" "$install_dir/.env"
}

write_updater_token() {
  local install_dir=$1
  install -d -m 0700 "$install_dir/secrets"
  if [[ ! -f $install_dir/secrets/updater-token ]]; then
    openssl rand -hex 32 > "$install_dir/secrets/updater-token"
  fi
  chmod 0444 "$install_dir/secrets/updater-token"
}

install_release_files() {
  local bundle_dir=$1
  local install_dir=$2
  install -d -m 0755 "$install_dir"
  install -m 0644 "$bundle_dir/compose.yaml" "$install_dir/compose.yaml"
  install -m 0644 "$bundle_dir/.env.example" "$install_dir/.env.example"
  install -m 0644 "$bundle_dir/upgrade-contract.json" "$install_dir/upgrade-contract.json"
  install -m 0644 "$bundle_dir/release-manifest.json" "$install_dir/release-manifest.json"
  install -m 0644 "$bundle_dir/release-manifest.sig" "$install_dir/release-manifest.sig"
  install -m 0644 "$bundle_dir/update-public-key.pem" "$install_dir/update-public-key.pem"
  install -m 0755 "$bundle_dir/appctl" "$install_dir/appctl"
}

wait_for_service_health() {
  local install_dir=$1
  local service=$2
  local timeout_seconds=${3:-120}
  local container_id status elapsed=0

  while (( elapsed < timeout_seconds )); do
    container_id=$(docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" ps -q "$service")
    if [[ -n $container_id ]]; then
      status=$(docker inspect "$container_id" --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}')
      [[ $status == healthy ]] && return 0
      [[ $status == exited || $status == dead || $status == unhealthy ]] && return 1
    fi
    sleep 2
    elapsed=$((elapsed + 2))
  done
  return 1
}
