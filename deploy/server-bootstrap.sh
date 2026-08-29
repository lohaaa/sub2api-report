#!/usr/bin/env bash
set -euo pipefail

repository=lohaaa/sub2api-report
requested_version=${SUB2API_REPORT_VERSION:-latest}

if [[ $(id -u) -ne 0 ]]; then
  echo "Run as root: curl -fsSL https://raw.githubusercontent.com/$repository/main/deploy/server-bootstrap.sh | sudo bash" >&2
  exit 2
fi

install_dependencies() {
  local missing=()
  for command_name in curl sha256sum tar; do
    command -v "$command_name" >/dev/null 2>&1 || missing+=("$command_name")
  done
  (( ${#missing[@]} == 0 )) && return

  echo "Installing required host tools: ${missing[*]}"
  if command -v apt-get >/dev/null 2>&1; then
    apt-get update
    DEBIAN_FRONTEND=noninteractive apt-get install --yes curl coreutils tar
  elif command -v dnf >/dev/null 2>&1; then
    dnf install --assumeyes curl coreutils tar
  elif command -v yum >/dev/null 2>&1; then
    yum install --assumeyes curl coreutils tar
  else
    echo "Install curl, coreutils, and tar, then retry." >&2
    exit 2
  fi
}

install_dependencies
install_runtime_dependencies() {
  if command -v apt-get >/dev/null 2>&1; then
    apt-get update
    DEBIAN_FRONTEND=noninteractive apt-get install --yes libicu-dev libssl-dev zlib1g
  elif command -v dnf >/dev/null 2>&1; then
    dnf install --assumeyes libicu openssl-libs zlib libstdc++
  elif command -v yum >/dev/null 2>&1; then
    yum install --assumeyes libicu openssl-libs zlib libstdc++
  else
    echo "Direct deployment supports Debian/Ubuntu and RHEL-compatible systemd distributions." >&2
    exit 2
  fi
}
install_runtime_dependencies
command -v systemctl >/dev/null 2>&1 || {
  echo "Direct server deployment requires a systemd-based Linux distribution." >&2
  exit 2
}

if [[ $requested_version == latest ]]; then
  release_url=$(curl --fail --silent --show-error --location \
    --output /dev/null --write-out '%{url_effective}' \
    "https://github.com/$repository/releases/latest")
  tag=${release_url##*/}
else
  tag="v${requested_version#v}"
fi
version=${tag#v}
if [[ ! $version =~ ^[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.-]+)?$ ]]; then
  echo "Could not determine a valid release version." >&2
  exit 1
fi

asset="sub2api-report-server-v${version}-linux-amd64.tar.gz"
base_url="https://github.com/$repository/releases/download/v${version}"
work_dir=$(mktemp -d /tmp/sub2api-report-server-bootstrap.XXXXXX)
trap 'rm -rf "$work_dir"' EXIT

printf 'Downloading Sub2API Report server package v%s...\n' "$version"
curl --fail --silent --show-error --location --retry 3 \
  --output "$work_dir/$asset" "$base_url/$asset"
curl --fail --silent --show-error --location --retry 3 \
  --output "$work_dir/checksums.txt" "$base_url/checksums.txt"
checksum_line=$(grep "  ${asset}$" "$work_dir/checksums.txt" || true)
[[ -n $checksum_line ]] || {
  echo "Release checksum does not contain $asset." >&2
  exit 1
}
(
  cd "$work_dir"
  printf '%s\n' "$checksum_line" | sha256sum --check --strict -
)

bundle_dir="$work_dir/bundle"
mkdir -p "$bundle_dir"
tar -xzf "$work_dir/$asset" -C "$bundle_dir"
"$bundle_dir/server-install.sh"
