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

validate_release_compatibility_file() {
  local compatibility_file=$1
  local version=$2
  local base_version source_version minimum_updater_version

  base_version=${version%%-*}
  jq -e --arg releaseVersion "$base_version" '
    (keys | sort) == ([
      "deploymentContractVersion",
      "manifestSchemaVersion",
      "manualUpgradeRequired",
      "minimumUpdaterVersion",
      "onlineInstallSupported",
      "onlineUpgradeFrom",
      "releaseVersion",
      "schemaVersion",
      "upgradeMessage"
    ] | sort)
    and .schemaVersion == 1
    and .releaseVersion == $releaseVersion
    and .manifestSchemaVersion == 3
    and .deploymentContractVersion == 1
    and (.minimumUpdaterVersion | type == "string" and test("^[0-9]+\\.[0-9]+\\.[0-9]+$"))
    and (.manualUpgradeRequired | type == "boolean")
    and (.onlineInstallSupported | type == "boolean")
    and (.onlineUpgradeFrom | type == "array")
    and (all(.onlineUpgradeFrom[]; type == "string" and test("^[0-9]+\\.[0-9]+\\.[0-9]+$")))
    and (.upgradeMessage | type == "string" and length > 0 and length <= 300)
    and (
      (.manualUpgradeRequired and (.onlineInstallSupported | not) and (.onlineUpgradeFrom | length == 0))
      or
      ((.manualUpgradeRequired | not) and .onlineInstallSupported and (.onlineUpgradeFrom | length > 0))
    )
  ' "$compatibility_file" >/dev/null || {
    echo "$compatibility_file is invalid or does not match release $base_version." >&2
    return 1
  }

  minimum_updater_version=$(jq -r '.minimumUpdaterVersion' "$compatibility_file")
  [[ $(printf '%s\n%s\n' "$minimum_updater_version" "$base_version" | sort -V | tail -n 1) == "$base_version" ]] || {
    echo "minimumUpdaterVersion cannot be newer than the target release." >&2
    return 1
  }
  while IFS= read -r source_version; do
    [[ $source_version != "$base_version" \
      && $(printf '%s\n%s\n' "$source_version" "$base_version" | sort -V | tail -n 1) == "$base_version" ]] || {
      echo "onlineUpgradeFrom must contain only older stable versions." >&2
      return 1
    }
  done < <(jq -r '.onlineUpgradeFrom[]' "$compatibility_file")
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
  [[ $config_path =~ ^(blobs/sha256/)?[a-f0-9]{64}(\.json)?$ ]] || {
    echo "$archive contains an invalid image config path." >&2
    return 1
  }
  config_hex=${config_path#blobs/sha256/}
  config_hex=${config_hex%.json}
  actual_config_digest="sha256:$(tar -xOzf "$archive" "$config_path" | sha256sum | awk '{print $1}')"
  [[ $actual_config_digest == "sha256:$config_hex" \
    && $actual_config_digest == "$expected_config_digest" ]] || {
    echo "$archive image config digest does not match the signed manifest." >&2
    return 1
  }
  index_json=$(tar -xOzf "$archive" index.json) || return
  jq -e '(.manifests | length) == 1' <<<"$index_json" >/dev/null || {
    echo "$archive index must contain exactly one target descriptor." >&2
    return 1
  }
  target_digest=$(jq -r '.manifests[0].digest' <<<"$index_json")
  [[ $target_digest == "$expected_target_digest" \
    && $target_digest =~ ^sha256:[a-f0-9]{64}$ ]] || {
    echo "$archive target digest does not match the signed manifest." >&2
    return 1
  }
  target_hex=${target_digest#sha256:}
  [[ $(tar -xOzf "$archive" "blobs/sha256/$target_hex" | sha256sum | awk '{print $1}') == "$target_hex" ]] || {
    echo "$archive target descriptor content does not match its digest." >&2
    return 1
  }
}

verify_release_bundle() {
  local bundle_dir=$1
  local app_archive="$bundle_dir/images/sub2api-report-app-linux-amd64.tar.gz"
  local updater_archive="$bundle_dir/images/sub2api-report-updater-linux-amd64.tar.gz"
  local expected_app_sha expected_updater_sha actual_app_sha actual_updater_sha
  local expected_app_id expected_updater_id expected_app_target expected_updater_target
  local expected_app_tag expected_updater_tag
  local expected_app_size expected_updater_size actual_app_size actual_updater_size manifest_version
  local expected_notes_sha expected_notes_size actual_notes_sha actual_notes_size release_heading

  for required_file in \
    "$bundle_dir/compose.yaml" \
    "$bundle_dir/.env.example" \
    "$bundle_dir/upgrade-contract.json" \
    "$bundle_dir/release-compatibility.json" \
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

  [[ $(jq -r '.schemaVersion' "$bundle_dir/release-manifest.json") == 3 \
    && $(jq -r '.channel' "$bundle_dir/release-manifest.json") == stable ]] || {
    echo "Unsupported release manifest schema or channel." >&2
    return 1
  }
  manifest_version=$(jq -r '.version' "$bundle_dir/release-manifest.json")
  [[ $manifest_version =~ ^[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.-]+)?$ ]] || {
    echo "Release manifest version is invalid." >&2
    return 1
  }
  validate_release_compatibility_file \
    "$bundle_dir/release-compatibility.json" "$manifest_version" || return
  jq -e --slurpfile policy "$bundle_dir/release-compatibility.json" '
    .schemaVersion == $policy[0].manifestSchemaVersion
    and .deploymentContractVersion == $policy[0].deploymentContractVersion
    and .minimumUpdaterVersion == $policy[0].minimumUpdaterVersion
    and .manualUpgradeRequired == $policy[0].manualUpgradeRequired
    and .onlineInstallSupported == $policy[0].onlineInstallSupported
    and .onlineUpgradeFrom == $policy[0].onlineUpgradeFrom
    and .upgradeMessage == $policy[0].upgradeMessage
  ' "$bundle_dir/release-manifest.json" >/dev/null || {
    echo "Signed manifest does not match release-compatibility.json." >&2
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
  expected_app_id=$(jq -r '.app.imageId' "$bundle_dir/release-manifest.json")
  expected_updater_id=$(jq -r '.updater.imageId' "$bundle_dir/release-manifest.json")
  expected_app_target=$(jq -r '.app.targetDigest' "$bundle_dir/release-manifest.json")
  expected_updater_target=$(jq -r '.updater.targetDigest' "$bundle_dir/release-manifest.json")
  expected_app_tag=$(jq -r '.app.loadedTag' "$bundle_dir/release-manifest.json")
  expected_updater_tag=$(jq -r '.updater.loadedTag' "$bundle_dir/release-manifest.json")
  echo "Verifying signed App image metadata..."
  verify_image_archive_metadata \
    "$app_archive" "$expected_app_tag" "$expected_app_id" "$expected_app_target"
  echo "Verifying signed Updater image metadata..."
  verify_image_archive_metadata \
    "$updater_archive" "$expected_updater_tag" "$expected_updater_id" "$expected_updater_target"
}

load_release_images() {
  local bundle_dir=$1
  echo "Loading App image into Docker Engine..."
  gzip -dc "$bundle_dir/images/sub2api-report-app-linux-amd64.tar.gz" | docker load
  echo "Loading Updater image into Docker Engine..."
  gzip -dc "$bundle_dir/images/sub2api-report-updater-linux-amd64.tar.gz" | docker load
}

validate_loaded_images() {
  local bundle_dir=$1
  local app_tag updater_tag app_id updater_id expected_app_id expected_updater_id
  local expected_app_target expected_updater_target
  local app_platform updater_platform app_version updater_version app_role updater_role manifest_version

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
  expected_app_target=$(jq -r '.app.targetDigest' "$bundle_dir/release-manifest.json")
  expected_updater_target=$(jq -r '.updater.targetDigest' "$bundle_dir/release-manifest.json")
  [[ $app_id == "$expected_app_id" || $app_id == "$expected_app_target" ]] || {
    printf 'Loaded App image ID %s matches neither signed config nor target digest.\n' "$app_id" >&2
    return 1
  }
  [[ $updater_id == "$expected_updater_id" || $updater_id == "$expected_updater_target" ]] || {
    printf 'Loaded Updater image ID %s matches neither signed config nor target digest.\n' "$updater_id" >&2
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
  app_role=$(docker image inspect "$app_tag" --format '{{index .Config.Labels "io.sub2api-report.role"}}')
  updater_role=$(docker image inspect "$updater_tag" --format '{{index .Config.Labels "io.sub2api-report.role"}}')
  [[ $app_version == "$manifest_version" && $updater_version == "$manifest_version" ]] || {
    echo "Loaded image versions do not match the signed release manifest." >&2
    return 1
  }
  [[ $app_role == app && $updater_role == updater ]] || {
    echo "Loaded image roles do not match the signed release contract." >&2
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
  local instance_id docker_gid app_port bind_address source_env

  [[ -S /var/run/docker.sock ]] || {
    echo "Docker Engine socket /var/run/docker.sock is required." >&2
    return 1
  }
  source_env="$install_dir/.env.example"
  if [[ -f $install_dir/.env ]]; then
    source_env="$install_dir/.env"
  fi

  instance_id=$(sed -n 's/^INSTANCE_ID=//p' "$source_env" | head -n 1)
  [[ -n $instance_id ]] || instance_id=$(openssl rand -hex 16)
  docker_gid=$(stat -c '%g' /var/run/docker.sock)
  app_port=${SUB2API_REPORT_PORT:-$(sed -n 's/^APP_PORT=//p' "$source_env" | head -n 1)}
  bind_address=${SUB2API_REPORT_BIND_ADDRESS:-$(sed -n 's/^BIND_ADDRESS=//p' "$source_env" | head -n 1)}
  app_port=${app_port:-8081}
  bind_address=${bind_address:-0.0.0.0}

  [[ $app_port =~ ^[1-9][0-9]{0,4}$ && $app_port -le 65535 ]] || {
    echo "SUB2API_REPORT_PORT must be an integer from 1 to 65535." >&2
    return 1
  }
  [[ $bind_address =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ \
    || $bind_address == localhost \
    || $bind_address =~ ^\[[0-9A-Fa-f:]+\]$ ]] || {
    echo "SUB2API_REPORT_BIND_ADDRESS must be an IPv4 address, localhost, or bracketed IPv6 address." >&2
    return 1
  }

  grep -Ev '^(INSTANCE_ID|DOCKER_GID|APP_PORT|BIND_ADDRESS)=' "$source_env" \
    > "$install_dir/.env.tmp"
  printf 'APP_PORT=%s\nBIND_ADDRESS=%s\nINSTANCE_ID=%s\nDOCKER_GID=%s\n' \
    "$app_port" "$bind_address" "$instance_id" "$docker_gid" >> "$install_dir/.env.tmp"
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
  install -m 0644 "$bundle_dir/release-compatibility.json" "$install_dir/release-compatibility.json"
  install -m 0644 "$bundle_dir/release-manifest.json" "$install_dir/release-manifest.json"
  install -m 0644 "$bundle_dir/release-manifest.sig" "$install_dir/release-manifest.sig"
  install -m 0644 "$bundle_dir/update-public-key.pem" "$install_dir/update-public-key.pem"
  install -m 0755 "$bundle_dir/appctl" "$install_dir/appctl"
}

resolve_service_image_id() {
  local install_dir=$1
  local service=$2
  local container_id image_id metadata status health upgrade_operation selected_image_id
  local -a container_ids=() image_ids=() original_image_ids=() healthy_image_ids=()

  mapfile -t container_ids < <(
    docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" \
      ps --all --quiet "$service"
  )
  for container_id in "${container_ids[@]}"; do
    image_id=$(docker inspect --format '{{.Image}}' "$container_id")
    [[ $image_id =~ ^sha256:[a-f0-9]{64}$ ]] || {
      echo "The $service container has an invalid image ID." >&2
      return 1
    }
    metadata=$(docker inspect --format \
      '{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}|{{with index .Config.Labels "io.sub2api-report.upgrade-operation"}}{{.}}{{end}}' \
      "$container_id")
    IFS='|' read -r status health upgrade_operation <<<"$metadata"
    image_ids+=("$image_id")
    [[ -n $upgrade_operation ]] || original_image_ids+=("$image_id")
    if [[ $status == running && $health == healthy ]]; then
      healthy_image_ids+=("$image_id")
    fi
  done

  if [[ ${#image_ids[@]} -eq 1 ]]; then
    selected_image_id=${image_ids[0]}
  elif [[ ${#original_image_ids[@]} -eq 1 ]]; then
    selected_image_id=${original_image_ids[0]}
  elif [[ ${#healthy_image_ids[@]} -eq 1 ]]; then
    selected_image_id=${healthy_image_ids[0]}
  else
    echo "Could not identify the previous $service container in $install_dir; remove stale candidate containers before retrying." >&2
    return 1
  fi
  printf '%s\n' "$selected_image_id"
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
