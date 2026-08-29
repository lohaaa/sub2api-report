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
runtime_dependencies_present() {
  command -v ldconfig >/dev/null 2>&1 || return 1
  local libraries
  libraries=$(ldconfig -p 2>/dev/null)
  for library in libicuuc.so libssl.so.3 libz.so.1 libstdc++.so.6 libgssapi_krb5.so.2 libunwind.so.8; do
    [[ $libraries == *"$library"* ]] || return 1
  done
}

install_runtime_dependencies() {
  runtime_dependencies_present && return
  echo "Installing missing .NET native runtime dependencies..."
  if command -v apt-get >/dev/null 2>&1; then
    apt-get update
    if apt-cache show dotnet-runtime-deps-10.0 >/dev/null 2>&1; then
      DEBIAN_FRONTEND=noninteractive apt-get install --yes --no-install-recommends \
        dotnet-runtime-deps-10.0
    else
      local icu_package ssl_package
      icu_package=$(apt-cache search --names-only '^libicu[0-9]+$' | awk '{print $1}' | sort -V | tail -n 1)
      for candidate in libssl3t64 libssl3; do
        if apt-cache show "$candidate" >/dev/null 2>&1; then
          ssl_package=$candidate
          break
        fi
      done
      [[ -n $icu_package && -n ${ssl_package:-} ]] || {
        echo "Could not resolve ICU/OpenSSL runtime packages for this distribution." >&2
        exit 2
      }
      DEBIAN_FRONTEND=noninteractive apt-get install --yes --no-install-recommends \
        "$icu_package" "$ssl_package" zlib1g libstdc++6 libgssapi-krb5-2 libunwind8
    fi
  elif command -v dnf >/dev/null 2>&1; then
    dnf install --assumeyes libicu openssl-libs zlib libstdc++ libunwind krb5-libs
  elif command -v yum >/dev/null 2>&1; then
    yum install --assumeyes libicu openssl-libs zlib libstdc++ libunwind krb5-libs
  else
    echo "Direct deployment supports Debian/Ubuntu and RHEL-compatible systemd distributions." >&2
    exit 2
  fi
  runtime_dependencies_present || {
    echo "Required native runtime libraries are still unavailable after installation." >&2
    exit 2
  }
}

github_curl() {
  curl --fail --silent --show-error --location \
    --retry 8 --retry-all-errors --retry-delay 3 --connect-timeout 20 \
    --speed-limit 1024 --speed-time 60 "$@"
}
install_runtime_dependencies
command -v systemctl >/dev/null 2>&1 || {
  echo "Direct server deployment requires a systemd-based Linux distribution." >&2
  exit 2
}

if [[ $requested_version == latest ]]; then
  release_url=$(github_curl --output /dev/null --write-out '%{url_effective}' \
    "https://github.com/$repository/releases/latest") || {
    echo "Could not reach GitHub Releases after multiple retries." >&2
    exit 1
  }
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
github_curl --output "$work_dir/$asset" "$base_url/$asset" || {
  echo "Server package download failed after multiple retries." >&2
  exit 1
}
github_curl --output "$work_dir/checksums.txt" "$base_url/checksums.txt" || {
  echo "Release checksum download failed after multiple retries." >&2
  exit 1
}
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
