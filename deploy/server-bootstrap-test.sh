#!/usr/bin/env bash
set -euo pipefail

repo_root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
test_root=$(mktemp -d /tmp/sub2api-report-server-bootstrap-test.XXXXXX)
cleanup() {
  rm -rf "$test_root"
}
trap cleanup EXIT

fixture_dir="$test_root/fixture"
mock_bin="$test_root/bin"
mkdir -p "$fixture_dir/bundle" "$mock_bin"
printf '#!/usr/bin/env bash\nexit 0\n' > "$fixture_dir/bundle/server-install.sh"
chmod 0755 "$fixture_dir/bundle/server-install.sh"
tar -czf "$fixture_dir/server.tar.gz" -C "$fixture_dir/bundle" .
printf '%s  sub2api-report-server-v1.0.4-linux-amd64.tar.gz\n' \
  "$(sha256sum "$fixture_dir/server.tar.gz" | cut -d ' ' -f 1)" > "$fixture_dir/checksums.txt"

cat > "$mock_bin/id" <<'EOF'
#!/bin/sh
if [ "${1:-}" = "-u" ]; then
  echo 1000
else
  exec /usr/bin/id "$@"
fi
EOF
cat > "$mock_bin/ldconfig" <<'EOF'
#!/bin/sh
cat <<'LIBRARIES'
libicuuc.so
libssl.so.3
libz.so.1
libstdc++.so.6
libgssapi_krb5.so.2
libunwind.so.8
LIBRARIES
EOF
cat > "$mock_bin/systemctl" <<'EOF'
#!/bin/sh
exit 0
EOF
cat > "$mock_bin/sudo" <<'EOF'
#!/bin/sh
printf '%s\n' "$*" >> "$BOOTSTRAP_TEST_ROOT/sudo.log"
exit 0
EOF
cat > "$mock_bin/curl" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
output=
write_out=
url=
while (( $# > 0 )); do
  case "$1" in
    --output)
      output=$2
      shift 2
      ;;
    --write-out)
      write_out=$2
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
printf '%s\n' "$url" >> "$BOOTSTRAP_TEST_ROOT/curl.log"
case "$url" in
  */releases/latest)
    printf 'https://github.com/lohaaa/sub2api-report/releases/tag/v1.0.4'
    ;;
  */sub2api-report-server-v1.0.4-linux-amd64.tar.gz)
    cp "$BOOTSTRAP_TEST_ROOT/fixture/server.tar.gz" "$output"
    ;;
  */checksums.txt)
    cp "$BOOTSTRAP_TEST_ROOT/fixture/checksums.txt" "$output"
    ;;
  *)
    echo "Unexpected curl URL: $url" >&2
    exit 1
    ;;
esac
: "$write_out"
EOF
chmod 0755 "$mock_bin"/*

export BOOTSTRAP_TEST_ROOT="$test_root"
export PATH="$mock_bin:$PATH"
export SUB2API_REPORT_VERSION=latest
export SUB2API_REPORT_PORT=18080
bash "$repo_root/deploy/server-bootstrap.sh" > "$test_root/output.log"

test "$(wc -l < "$test_root/sudo.log")" -eq 1
grep -Eq '^env SUB2API_REPORT_PORT=18080 bash /tmp/sub2api-report-server-bootstrap\..*/bundle/server-install\.sh$' \
  "$test_root/sudo.log"
test "$(wc -l < "$test_root/curl.log")" -eq 3
grep -q '^Release resolved: v1.0.4$' "$test_root/output.log"
grep -q '^Server package (attempt 1/8, resume supported)$' "$test_root/output.log"
grep -q '^Verifying SHA-256 checksum\.\.\.$' "$test_root/output.log"
grep -q '^Installing systemd service\.\.\.$' "$test_root/output.log"

echo "server bootstrap tests passed"
