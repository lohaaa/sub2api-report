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

verify_image_archive_metadata() {
  local archive=$1
  local expected_tag=$2
  local expected_config_digest=$3
  local expected_target_digest=$4
  local docker_manifest index_json config_path config_hex actual_config_digest target_digest target_hex
  docker_manifest=$(tar -xOzf "$archive" manifest.json) || return
  jq -e --arg tag "$expected_tag" \
    'length == 1 and .[0].RepoTags == [$tag] and (.[0].Layers | length > 0)' \
    <<<"$docker_manifest" >/dev/null || {
    echo "$archive does not contain exactly the signed image tag." >&2
    return 1
  }
  config_path=$(jq -r '.[0].Config' <<<"$docker_manifest")
  [[ $config_path =~ ^(blobs/sha256/)?[a-f0-9]{64}(\.json)?$ ]] || return 1
  config_hex=${config_path#blobs/sha256/}
  config_hex=${config_hex%.json}
  actual_config_digest="sha256:$(tar -xOzf "$archive" "$config_path" | sha256sum | awk '{print $1}')"
  [[ $actual_config_digest == "sha256:$config_hex" \
    && $actual_config_digest == "$expected_config_digest" ]] || {
    echo "$archive image config digest does not match the signed manifest." >&2
    return 1
  }
  index_json=$(tar -xOzf "$archive" index.json) || return
  jq -e '(.manifests | length) == 1' <<<"$index_json" >/dev/null || return 1
  target_digest=$(jq -r '.manifests[0].digest' <<<"$index_json")
  [[ $target_digest == "$expected_target_digest" \
    && $target_digest =~ ^sha256:[a-f0-9]{64}$ ]] || {
    echo "$archive target digest does not match the signed manifest." >&2
    return 1
  }
  target_hex=${target_digest#sha256:}
  [[ $(tar -xOzf "$archive" "blobs/sha256/$target_hex" | sha256sum | awk '{print $1}') == "$target_hex" ]] || return 1
}

verify_release_bundle() {
  local bundle_dir=$1
  local app_archive="$bundle_dir/images/sub2api-report-app-linux-amd64.tar.gz"
  local manifest="$bundle_dir/release-manifest.json"
  local manifest_version release_heading expected_sha expected_size actual_sha actual_size
  local expected_notes_sha expected_notes_size actual_notes_sha actual_notes_size
  local expected_app_id expected_app_target expected_app_tag

  for required_file in \
    "$bundle_dir/compose.yaml" \
    "$bundle_dir/.env.example" \
    "$bundle_dir/upgrade-contract.json" \
    "$bundle_dir/CHANGELOG.md" \
    "$bundle_dir/LICENSE" \
    "$bundle_dir/RELEASE-NOTES.md" \
    "$manifest" \
    "$bundle_dir/release-manifest.sig" \
    "$bundle_dir/update-public-key.pem" \
    "$bundle_dir/images/checksums.txt" \
    "$app_archive"; do
    [[ -f $required_file ]] || {
      echo "Release bundle is missing ${required_file#"$bundle_dir"/}." >&2
      return 1
    }
  done
  (cd "$bundle_dir/images" && sha256sum --check --strict checksums.txt)
  openssl dgst -sha256 \
    -verify "$bundle_dir/update-public-key.pem" \
    -signature "$bundle_dir/release-manifest.sig" \
    "$manifest" >/dev/null
  jq -e '
    .schemaVersion == 4
    and .channel == "stable"
    and .architecture == "linux/amd64"
    and .deploymentContractVersion == 2
    and .signatureAlgorithm == "RSASSA-PKCS1-v1_5-SHA256"
    and (has("updater") | not)
    and (has("onlineInstallSupported") | not)
  ' "$manifest" >/dev/null || {
    echo "Unsupported App-only release manifest." >&2
    return 1
  }
  jq -e --slurpfile contract "$bundle_dir/upgrade-contract.json" '
    .deploymentContractVersion == $contract[0].deploymentContractVersion
    and $contract[0].schemaVersion == 2
    and $contract[0].update.method == "host-bootstrap"
  ' "$manifest" >/dev/null || {
    echo "Release manifest and deployment contract do not match." >&2
    return 1
  }
  manifest_version=$(jq -r '.version' "$manifest")
  [[ $manifest_version =~ ^[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.-]+)?$ ]] || return 1
  IFS= read -r release_heading < "$bundle_dir/RELEASE-NOTES.md"
  [[ $release_heading == "## [$manifest_version]" \
    || $release_heading == "## [$manifest_version] - "* ]] || return 1

  expected_sha=$(jq -r '.app.archiveSha256' "$manifest")
  expected_size=$(jq -r '.app.size' "$manifest")
  actual_sha=$(sha256sum "$app_archive" | awk '{print $1}')
  actual_size=$(stat -c '%s' "$app_archive")
  [[ $actual_sha == "$expected_sha" && $actual_size == "$expected_size" ]] || {
    echo "App archive does not match the signed release manifest." >&2
    return 1
  }
  expected_notes_sha=$(jq -r '.releaseNotes.sha256' "$manifest")
  expected_notes_size=$(jq -r '.releaseNotes.size' "$manifest")
  actual_notes_sha=$(sha256sum "$bundle_dir/RELEASE-NOTES.md" | awk '{print $1}')
  actual_notes_size=$(stat -c '%s' "$bundle_dir/RELEASE-NOTES.md")
  [[ $actual_notes_sha == "$expected_notes_sha" && $actual_notes_size == "$expected_notes_size" ]] || return 1

  expected_app_id=$(jq -r '.app.imageId' "$manifest")
  expected_app_target=$(jq -r '.app.targetDigest' "$manifest")
  expected_app_tag=$(jq -r '.app.loadedTag' "$manifest")
  echo "Verifying signed App image metadata..."
  verify_image_archive_metadata \
    "$app_archive" "$expected_app_tag" "$expected_app_id" "$expected_app_target"
}

load_release_images() {
  local bundle_dir=$1
  echo "Loading App image into Docker Engine..."
  gzip -dc "$bundle_dir/images/sub2api-report-app-linux-amd64.tar.gz" | docker load
}

validate_loaded_images() {
  local bundle_dir=$1
  local manifest="$bundle_dir/release-manifest.json"
  local version app_tag app_id expected_id expected_target platform image_version role contract
  version=$(jq -r '.version' "$manifest")
  app_tag=$(jq -r '.app.loadedTag' "$manifest")
  [[ $app_tag == "sub2api-report-app:$version" ]] || return 1
  app_id=$(docker image inspect "$app_tag" --format '{{.Id}}')
  expected_id=$(jq -r '.app.imageId' "$manifest")
  expected_target=$(jq -r '.app.targetDigest' "$manifest")
  [[ $app_id == "$expected_id" || $app_id == "$expected_target" ]] || return 1
  platform=$(docker image inspect "$app_tag" --format '{{.Os}}/{{.Architecture}}')
  image_version=$(docker image inspect "$app_tag" --format '{{index .Config.Labels "org.opencontainers.image.version"}}')
  role=$(docker image inspect "$app_tag" --format '{{index .Config.Labels "io.sub2api-report.role"}}')
  contract=$(docker image inspect "$app_tag" --format '{{index .Config.Labels "io.sub2api-report.contract"}}')
  [[ $platform == linux/amd64 && $image_version == "$version" && $role == app && $contract == 2 ]]
}

activate_loaded_images() {
  local bundle_dir=$1
  local app_tag
  app_tag=$(jq -r '.app.loadedTag' "$bundle_dir/release-manifest.json")
  docker tag "$app_tag" sub2api-report-app:current
}

write_instance_env() {
  local install_dir=$1
  local app_port bind_address source_env
  source_env="$install_dir/.env.example"
  [[ ! -f $install_dir/.env ]] || source_env="$install_dir/.env"
  app_port=${SUB2API_REPORT_PORT:-$(sed -n 's/^APP_PORT=//p' "$source_env" | head -n 1)}
  bind_address=${SUB2API_REPORT_BIND_ADDRESS:-$(sed -n 's/^BIND_ADDRESS=//p' "$source_env" | head -n 1)}
  app_port=${app_port:-8081}
  bind_address=${bind_address:-0.0.0.0}
  [[ $app_port =~ ^[1-9][0-9]{0,4}$ && $app_port -le 65535 ]] || return 1
  [[ $bind_address =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ \
    || $bind_address == localhost \
    || $bind_address =~ ^\[[0-9A-Fa-f:]+\]$ ]] || return 1
  {
    grep -Ev '^(APP_PORT|BIND_ADDRESS|INSTANCE_ID|DOCKER_GID)=' "$source_env" || true
  } > "$install_dir/.env.tmp"
  printf 'APP_PORT=%s\nBIND_ADDRESS=%s\n' "$app_port" "$bind_address" \
    >> "$install_dir/.env.tmp"
  chmod 0600 "$install_dir/.env.tmp"
  mv "$install_dir/.env.tmp" "$install_dir/.env"
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

resolve_service_container_id() {
  local install_dir=$1
  local service=$2
  local container_id metadata status health upgrade_operation selected_container_id
  local -a container_ids=() original_container_ids=() healthy_container_ids=()
  mapfile -t container_ids < <(
    docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" \
      ps --all --quiet "$service"
  )
  for container_id in "${container_ids[@]}"; do
    metadata=$(docker inspect --format \
      '{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}|{{with index .Config.Labels "io.sub2api-report.upgrade-operation"}}{{.}}{{end}}' \
      "$container_id")
    IFS='|' read -r status health upgrade_operation <<<"$metadata"
    [[ -n $upgrade_operation ]] || original_container_ids+=("$container_id")
    if [[ $status == running && $health == healthy ]]; then
      healthy_container_ids+=("$container_id")
    fi
  done
  if [[ ${#container_ids[@]} -eq 1 ]]; then
    selected_container_id=${container_ids[0]}
  elif [[ ${#original_container_ids[@]} -eq 1 ]]; then
    selected_container_id=${original_container_ids[0]}
  elif [[ ${#healthy_container_ids[@]} -eq 1 ]]; then
    selected_container_id=${healthy_container_ids[0]}
  else
    echo "Could not identify the previous $service container in $install_dir; remove stale candidate containers before retrying." >&2
    return 1
  fi
  printf '%s\n' "$selected_container_id"
}

resolve_service_image_id() {
  local install_dir=$1
  local service=$2
  local container_id image_id
  container_id=$(resolve_service_container_id "$install_dir" "$service") || return
  image_id=$(docker inspect --format '{{.Image}}' "$container_id")
  [[ $image_id =~ ^sha256:[a-f0-9]{64}$ ]] || return 1
  printf '%s\n' "$image_id"
}

wait_for_service_health() {
  local install_dir=$1
  local service=$2
  local timeout_seconds=${3:-120}
  local container_id status deadline
  deadline=$((SECONDS + timeout_seconds))
  while (( SECONDS < deadline )); do
    container_id=$(docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" ps -q "$service")
    if [[ -n $container_id ]]; then
      status=$(docker inspect "$container_id" --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}')
      [[ $status == healthy ]] && return 0
      [[ $status == exited || $status == dead || $status == unhealthy ]] && return 1
    fi
    sleep 2
  done
  return 1
}
