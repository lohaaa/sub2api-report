#!/usr/bin/env bash
set -euo pipefail

repository=lohaaa/sub2api-report
install_dir=${SUB2API_REPORT_INSTALL_DIR:-/opt/sub2api-report}
requested_version=${SUB2API_REPORT_VERSION:-latest}
start_services=${SUB2API_REPORT_START:-true}
if [[ $start_services != true && $start_services != false ]]; then
  echo "SUB2API_REPORT_START must be true or false." >&2
  exit 2
fi

if [[ $(id -u) -eq 0 && -z ${SUDO_USER:-} ]]; then
  echo "Warning: running as root; downloads use root network settings." >&2
  echo "Prefer running this bootstrap from a regular sudo-capable user." >&2
fi
run_as_root() {
  if [[ $(id -u) -eq 0 ]]; then
    "$@"
    return
  fi
  command -v sudo >/dev/null 2>&1 || {
    echo "sudo is required to install Docker deployment files and images." >&2
    exit 2
  }
  sudo "$@"
}
install_host_dependencies() {
  local missing=()
  for command_name in curl gzip jq openssl sha256sum stat tar; do
    command -v "$command_name" >/dev/null 2>&1 || missing+=("$command_name")
  done
  (( ${#missing[@]} == 0 )) && return

  echo "Installing required host tools: ${missing[*]}"
  if command -v apt-get >/dev/null 2>&1; then
    run_as_root apt-get update
    run_as_root env DEBIAN_FRONTEND=noninteractive \
      apt-get install --yes curl gzip jq openssl coreutils tar
  elif command -v dnf >/dev/null 2>&1; then
    run_as_root dnf install --assumeyes curl gzip jq openssl coreutils tar
  elif command -v yum >/dev/null 2>&1; then
    run_as_root yum install --assumeyes curl gzip jq openssl coreutils tar
  elif command -v apk >/dev/null 2>&1; then
    run_as_root apk add --no-cache curl gzip jq openssl coreutils tar
  else
    echo "Install curl, gzip, jq, openssl, coreutils, and tar, then retry." >&2
    exit 2
  fi
}

install_host_dependencies
command -v docker >/dev/null 2>&1 || {
  echo "Docker Engine is required: https://docs.docker.com/engine/install/" >&2
  exit 2
}
run_as_root docker info >/dev/null || {
  echo "Docker Engine is not running or is not accessible." >&2
  exit 2
}
run_as_root docker compose version >/dev/null || {
  echo "Docker Compose v2 is required." >&2
  exit 2
}

github_curl() {
  curl --fail --silent --show-error --location \
    --retry 8 --retry-all-errors --retry-delay 3 --connect-timeout 30 "$@"
}

download_with_progress() {
  local url=$1
  local output=$2
  local label=$3
  local attempt=1
  local maximum_attempts=8
  touch "$output"
  while (( attempt <= maximum_attempts )); do
    printf '\n%s (attempt %d/%d, resume supported)\n' "$label" "$attempt" "$maximum_attempts"
    if curl --fail --show-error --location --progress-bar \
      --continue-at - --connect-timeout 30 --speed-limit 128 --speed-time 300 \
      --output "$output" "$url"; then
      printf '%s complete: %s bytes\n' "$label" "$(stat -c '%s' "$output")"
      return
    fi
    printf 'Download interrupted at %s bytes. Retrying in 3 seconds...\n' \
      "$(stat -c '%s' "$output")" >&2
    attempt=$((attempt + 1))
    sleep 3
  done
  echo "$label failed after $maximum_attempts attempts." >&2
  return 1
}
if [[ $requested_version == latest ]]; then
  release_url=$(github_curl --output /dev/null --write-out '%{url_effective}' \
    "https://github.com/$repository/releases/latest") || {
    echo "Could not reach GitHub Releases after multiple retries." >&2
    exit 1
  }
  tag=${release_url##*/}
else
  tag=${requested_version#v}
  tag="v$tag"
fi

version=${tag#v}
if [[ ! $version =~ ^[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.-]+)?$ ]]; then
  echo "Could not determine a valid release version." >&2
  exit 1
fi

asset="sub2api-report-v${version}-linux-amd64.tar.gz"
base_url="https://github.com/$repository/releases/download/v${version}"
work_dir=$(mktemp -d /tmp/sub2api-report-bootstrap.XXXXXX)
cleanup() {
  rm -rf "$work_dir"
}
trap cleanup EXIT

printf 'Release resolved: v%s\n' "$version"
download_with_progress \
  "$base_url/$asset" \
  "$work_dir/$asset" \
  "Docker deployment bundle"
printf '\nDownloading checksum metadata...\n'
github_curl --output "$work_dir/checksums.txt" "$base_url/checksums.txt" || {
  echo "Release checksum download failed after multiple retries." >&2
  exit 1
}

checksum_line=$(grep "  ${asset}$" "$work_dir/checksums.txt" || true)
if [[ -z $checksum_line ]]; then
  echo "Release checksum does not contain $asset." >&2
  exit 1
fi
printf 'Verifying SHA-256 checksum...\n'
(
  cd "$work_dir"
  printf '%s\n' "$checksum_line" | sha256sum --check --strict -
)

bundle_dir="$work_dir/bundle"
mkdir -p "$bundle_dir"
printf 'Extracting Docker deployment bundle...\n'
tar -xzf "$work_dir/$asset" -C "$bundle_dir"

if [[ -f $install_dir/compose.yaml ]]; then
  installed_version=$(run_as_root jq -r '.version // empty' \
    "$install_dir/release-manifest.json" 2>/dev/null || true)
  if [[ $installed_version == "$version" ]]; then
    printf 'Sub2API Report v%s is already prepared.\n' "$version"
    if [[ $start_services == true ]]; then
      run_as_root docker compose --project-directory "$install_dir" \
        -f "$install_dir/compose.yaml" up -d --no-build
    fi
    exit 0
  fi
  echo "Updating the existing installation in $install_dir..."
  run_as_root env SUB2API_REPORT_INSTALL_DIR="$install_dir" \
    SUB2API_REPORT_START="$start_services" bash "$bundle_dir/update.sh"
else
  echo "Preparing a new installation in $install_dir..."
  legacy_prepare=false
  if [[ $start_services == false ]] \
    && ! grep -q 'SUB2API_REPORT_START' "$bundle_dir/install.sh"; then
    legacy_prepare=true
  fi
  run_as_root env SUB2API_REPORT_INSTALL_DIR="$install_dir" \
    SUB2API_REPORT_START="$start_services" bash "$bundle_dir/install.sh"
  if [[ $legacy_prepare == true ]]; then
    run_as_root docker compose --project-directory "$install_dir" \
      -f "$install_dir/compose.yaml" down
  fi
fi

if [[ $start_services == false ]]; then
  printf '\nSub2API Report v%s is prepared.\n' "$version"
  printf 'Start: cd %s && sudo docker compose up -d\n' "$install_dir"
  printf 'Logs:  cd %s && sudo docker compose logs -f app\n' "$install_dir"
else
  printf '\nSub2API Report v%s is ready.\n' "$version"
  app_port=$(run_as_root sed -n 's/^APP_PORT=//p' "$install_dir/.env")
  printf 'Open: http://<server>:%s\n' "$app_port"
  printf 'Logs: cd %s && sudo docker compose logs -f app\n' "$install_dir"
fi
