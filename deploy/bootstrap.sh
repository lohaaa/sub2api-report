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

if [[ $(id -u) -ne 0 ]]; then
  echo "Run as root: curl -fsSL https://raw.githubusercontent.com/$repository/main/deploy/bootstrap.sh | sudo bash" >&2
  exit 2
fi

install_host_dependencies() {
  local missing=()
  for command_name in curl gzip jq openssl sha256sum stat tar; do
    command -v "$command_name" >/dev/null 2>&1 || missing+=("$command_name")
  done
  (( ${#missing[@]} == 0 )) && return

  echo "Installing required host tools: ${missing[*]}"
  if command -v apt-get >/dev/null 2>&1; then
    apt-get update
    DEBIAN_FRONTEND=noninteractive apt-get install --yes curl gzip jq openssl coreutils tar
  elif command -v dnf >/dev/null 2>&1; then
    dnf install --assumeyes curl gzip jq openssl coreutils tar
  elif command -v yum >/dev/null 2>&1; then
    yum install --assumeyes curl gzip jq openssl coreutils tar
  elif command -v apk >/dev/null 2>&1; then
    apk add --no-cache curl gzip jq openssl coreutils tar
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
docker info >/dev/null 2>&1 || {
  echo "Docker Engine is not running or is not accessible." >&2
  exit 2
}
docker compose version >/dev/null 2>&1 || {
  echo "Docker Compose v2 is required." >&2
  exit 2
}

if [[ $requested_version == latest ]]; then
  release_url=$(curl --fail --silent --show-error --location \
    --output /dev/null --write-out '%{url_effective}' \
    "https://github.com/$repository/releases/latest")
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

printf 'Downloading Sub2API Report v%s...\n' "$version"
curl --fail --silent --show-error --location --retry 3 \
  --output "$work_dir/$asset" "$base_url/$asset"
curl --fail --silent --show-error --location --retry 3 \
  --output "$work_dir/checksums.txt" "$base_url/checksums.txt"

checksum_line=$(grep "  ${asset}$" "$work_dir/checksums.txt" || true)
if [[ -z $checksum_line ]]; then
  echo "Release checksum does not contain $asset." >&2
  exit 1
fi
(
  cd "$work_dir"
  printf '%s\n' "$checksum_line" | sha256sum --check --strict -
)

bundle_dir="$work_dir/bundle"
mkdir -p "$bundle_dir"
tar -xzf "$work_dir/$asset" -C "$bundle_dir"

if [[ -f $install_dir/compose.yaml ]]; then
  installed_version=$(jq -r '.version // empty' "$install_dir/release-manifest.json" 2>/dev/null || true)
  if [[ $installed_version == "$version" ]]; then
    printf 'Sub2API Report v%s is already prepared.\n' "$version"
    if [[ $start_services == true ]]; then
      docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" up -d --no-build
    fi
    exit 0
  fi
  echo "Updating the existing installation in $install_dir..."
  SUB2API_REPORT_INSTALL_DIR="$install_dir" "$bundle_dir/update.sh"
else
  echo "Preparing a new installation in $install_dir..."
  legacy_prepare=false
  if [[ $start_services == false ]] \
    && ! grep -q 'SUB2API_REPORT_START' "$bundle_dir/install.sh"; then
    legacy_prepare=true
  fi
  SUB2API_REPORT_INSTALL_DIR="$install_dir" "$bundle_dir/install.sh"
  if [[ $legacy_prepare == true ]]; then
    docker compose --project-directory "$install_dir" -f "$install_dir/compose.yaml" down
  fi
fi

if [[ $start_services == false ]]; then
  printf '\nSub2API Report v%s is prepared.\n' "$version"
  printf 'Start: cd %s && sudo docker compose up -d\n' "$install_dir"
  printf 'Logs:  cd %s && sudo docker compose logs -f app\n' "$install_dir"
else
  printf '\nSub2API Report v%s is ready.\n' "$version"
  printf 'Open: http://<server>:%s\n' "$(sed -n 's/^APP_PORT=//p' "$install_dir/.env")"
  printf 'Logs: cd %s && sudo docker compose logs -f app\n' "$install_dir"
fi
