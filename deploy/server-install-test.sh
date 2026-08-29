#!/usr/bin/env bash
set -euo pipefail

repo_root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
test_root=$(mktemp -d /tmp/sub2api-report-server-install-test.XXXXXX)
cleanup() {
  rm -rf "$test_root"
}
trap cleanup EXIT

bundle="$test_root/bundle"
mock_bin="$test_root/bin"
mkdir -p "$bundle/app" "$bundle/migrator" "$bundle/cli" "$mock_bin"
printf '1.0.1\n' > "$bundle/VERSION"
printf 'license\n' > "$bundle/LICENSE"
printf 'changelog\n' > "$bundle/CHANGELOG.md"
for executable in app/Sub2ApiReport.Api migrator/Sub2ApiReport.Migrator cli/Sub2ApiReport.Cli; do
  printf '#!/bin/sh\nexit 0\n' > "$bundle/$executable"
  chmod 0755 "$bundle/$executable"
done
cp "$repo_root/deploy/server-install.sh" "$bundle/server-install.sh"
chmod 0755 "$bundle/server-install.sh"

cat > "$mock_bin/id" <<'EOF'
#!/bin/sh
if [ "${1:-}" = "-u" ]; then
  echo 0
else
  exec /usr/bin/id "$@"
fi
EOF
cat > "$mock_bin/systemctl" <<EOF
#!/bin/sh
printf '%s\n' "\$*" >> "$test_root/systemctl.log"
exit 0
EOF
cat > "$mock_bin/curl" <<'EOF'
#!/bin/sh
exit 0
EOF
cat > "$mock_bin/chown" <<'EOF'
#!/bin/sh
exit 0
EOF
chmod 0755 "$mock_bin"/*

user=$(id -un)
export PATH="$mock_bin:$PATH"
export SUB2API_REPORT_SERVER_ROOT="$test_root/opt"
export SUB2API_REPORT_DATA_DIR="$test_root/data"
export SUB2API_REPORT_CONFIG_DIR="$test_root/etc"
export SUB2API_REPORT_BACKUP_DIR="$test_root/backups"
export SUB2API_REPORT_SYSTEMD_DIR="$test_root/systemd"
export SUB2API_REPORT_CONTROL_PATH="$test_root/usr/sub2api-reportctl"
export SUB2API_REPORT_SERVICE_USER="$user"
export SUB2API_REPORT_SERVICE_NAME=sub2api-report-test.service

"$bundle/server-install.sh"

test -L "$test_root/opt/current"
test "$(readlink -f "$test_root/opt/current")" = "$test_root/opt/releases/1.0.1"
test -x "$test_root/opt/current/app/Sub2ApiReport.Api"
test -f "$test_root/systemd/sub2api-report-test.service"
test -x "$test_root/usr/sub2api-reportctl"
grep -q "ExecStart=$test_root/opt/current/app/Sub2ApiReport.Api" \
  "$test_root/systemd/sub2api-report-test.service"
grep -q '^enable sub2api-report-test.service$' "$test_root/systemctl.log"
grep -q '^restart sub2api-report-test.service$' "$test_root/systemctl.log"

"$bundle/server-install.sh"
grep -q '^enable --now sub2api-report-test.service$' "$test_root/systemctl.log"

echo "server installer tests passed"
