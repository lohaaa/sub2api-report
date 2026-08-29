#!/usr/bin/env bash
set -euo pipefail

repo_root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
test_root=$(mktemp -d /tmp/sub2api-report-docker-bootstrap-test.XXXXXX)
cleanup() {
  rm -rf "$test_root"
}
trap cleanup EXIT

fixture_dir="$test_root/fixture"
mock_bin="$test_root/bin"
mkdir -p "$fixture_dir/bundle" "$mock_bin"
printf '#!/usr/bin/env bash\n# SUB2API_REPORT_START\nexit 0\n' > "$fixture_dir/bundle/install.sh"
printf '#!/usr/bin/env bash\nexit 0\n' > "$fixture_dir/bundle/update.sh"
chmod 0755 "$fixture_dir/bundle/install.sh" "$fixture_dir/bundle/update.sh"
tar -czf "$fixture_dir/bundle.tar.gz" -C "$fixture_dir/bundle" .
printf '%s  sub2api-report-v1.0.6-linux-amd64.tar.gz\n' \
  "$(sha256sum "$fixture_dir/bundle.tar.gz" | cut -d ' ' -f 1)" > "$fixture_dir/checksums.txt"

cat > "$mock_bin/id" <<'EOF'
#!/bin/sh
if [ "${1:-}" = "-u" ]; then
  echo 1000
else
  exec /usr/bin/id "$@"
fi
EOF
cat > "$mock_bin/docker" <<'EOF'
#!/bin/sh
exit 0
EOF
cat > "$mock_bin/sudo" <<'EOF'
#!/bin/sh
printf '%s\n' "$*" >> "$DOCKER_BOOTSTRAP_TEST_ROOT/sudo.log"
exit 0
EOF
cat > "$mock_bin/curl" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
output=
url=
while (( $# > 0 )); do
  case "$1" in
    --output)
      output=$2
      shift 2
      ;;
    --write-out)
      shift 2
      ;;
    --continue-at|--connect-timeout|--speed-limit|--speed-time|--retry|--retry-delay)
      shift 2
      ;;
    --fail|--silent|--show-error|--location|--progress-bar|--retry-all-errors)
      shift
      ;;
    *)
      url=$1
      shift
      ;;
  esac
done
printf '%s\n' "$url" >> "$DOCKER_BOOTSTRAP_TEST_ROOT/curl.log"
case "$url" in
  */releases/latest)
    printf 'https://github.com/lohaaa/sub2api-report/releases/tag/v1.0.6'
    ;;
  */sub2api-report-v1.0.6-linux-amd64.tar.gz)
    cp "$DOCKER_BOOTSTRAP_TEST_ROOT/fixture/bundle.tar.gz" "$output"
    ;;
  */checksums.txt)
    cp "$DOCKER_BOOTSTRAP_TEST_ROOT/fixture/checksums.txt" "$output"
    ;;
  *)
    echo "Unexpected curl URL: $url" >&2
    exit 1
    ;;
esac
EOF
chmod 0755 "$mock_bin"/*

export DOCKER_BOOTSTRAP_TEST_ROOT="$test_root"
export PATH="$mock_bin:$PATH"
export SUB2API_REPORT_INSTALL_DIR="$test_root/install"
export SUB2API_REPORT_START=false
bash "$repo_root/deploy/bootstrap.sh" > "$test_root/output.log"

test "$(wc -l < "$test_root/curl.log")" -eq 3
test "$(wc -l < "$test_root/sudo.log")" -eq 3
grep -q '^docker info$' "$test_root/sudo.log"
grep -q '^docker compose version$' "$test_root/sudo.log"
grep -Eq "^env SUB2API_REPORT_INSTALL_DIR=$test_root/install SUB2API_REPORT_START=false bash /tmp/sub2api-report-bootstrap\\..*/bundle/install\\.sh$" \
  "$test_root/sudo.log"
grep -q '^Release resolved: v1.0.6$' "$test_root/output.log"
grep -q '^Docker deployment bundle (attempt 1/8, resume supported)$' "$test_root/output.log"
grep -q '^Verifying SHA-256 checksum\.\.\.$' "$test_root/output.log"
grep -q '^Sub2API Report v1.0.6 is prepared\.$' "$test_root/output.log"

echo "docker bootstrap tests passed"
