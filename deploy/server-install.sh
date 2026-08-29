#!/usr/bin/env bash
set -euo pipefail

bundle_dir=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)
version=$(tr -d '[:space:]' < "$bundle_dir/VERSION")
install_root=${SUB2API_REPORT_SERVER_ROOT:-/opt/sub2api-report-server}
data_dir=${SUB2API_REPORT_DATA_DIR:-/var/lib/sub2api-report}
config_dir=${SUB2API_REPORT_CONFIG_DIR:-/etc/sub2api-report}
backup_root=${SUB2API_REPORT_BACKUP_DIR:-/var/backups/sub2api-report}
service_name=${SUB2API_REPORT_SERVICE_NAME:-sub2api-report.service}
service_user=${SUB2API_REPORT_SERVICE_USER:-sub2api-report}
systemd_dir=${SUB2API_REPORT_SYSTEMD_DIR:-/etc/systemd/system}
control_path=${SUB2API_REPORT_CONTROL_PATH:-/usr/local/bin/sub2api-reportctl}
release_dir="$install_root/releases/$version"
current_link="$install_root/current"

if [[ $(id -u) -ne 0 ]]; then
  echo "Run this installer as root." >&2
  exit 2
fi
[[ $(uname -s) == Linux && $(uname -m) == x86_64 ]] || {
  echo "The server package supports Linux amd64 only." >&2
  exit 2
}
command -v systemctl >/dev/null 2>&1 || {
  echo "systemd is required for direct server deployment." >&2
  exit 2
}
for command_name in curl install ln tar; do
  command -v "$command_name" >/dev/null 2>&1 || {
    echo "$command_name is required." >&2
    exit 2
  }
done
for required in runtime/Sub2ApiReport.Api runtime/Sub2ApiReport.Migrator runtime/Sub2ApiReport.Cli; do
  [[ -x $bundle_dir/$required ]] || {
    echo "Server package is missing $required." >&2
    exit 1
  }
done

echo "[1/5] Preparing service user and data directories..."
if ! getent group "$service_user" >/dev/null 2>&1; then
  groupadd --system "$service_user"
fi
if ! id "$service_user" >/dev/null 2>&1; then
  useradd --system --gid "$service_user" --home-dir "$data_dir" --shell /usr/sbin/nologin "$service_user"
fi

install -d -m 0755 "$install_root/releases"
install -d -m 0700 -o "$service_user" -g "$service_user" \
  "$data_dir" "$data_dir/db" "$data_dir/keys" "$data_dir/reports" "$data_dir/temp"
install -d -m 0755 "$config_dir"
install -d -m 0700 "$backup_root"
install -d -m 0755 "$systemd_dir" "$(dirname "$control_path")"

requested_port=${SUB2API_REPORT_PORT:-}
configured_port=
if [[ -f $config_dir/environment ]]; then
  configured_urls=$(sed -n 's/^ASPNETCORE_URLS=//p' "$config_dir/environment" | tail -n 1)
  configured_urls=${configured_urls#\"}
  configured_urls=${configured_urls%\"}
  if [[ $configured_urls =~ ^http://0\.0\.0\.0:([0-9]+)$ ]]; then
    configured_port=${BASH_REMATCH[1]}
  elif [[ -z $requested_port && -n $configured_urls ]]; then
    echo "Could not determine the existing App port from $config_dir/environment." >&2
    echo "Retry with SUB2API_REPORT_PORT=<port>." >&2
    exit 2
  fi
fi
app_port=${requested_port:-${configured_port:-8081}}
if [[ ! $app_port =~ ^[0-9]+$ ]] || (( 10#$app_port < 1 || 10#$app_port > 65535 )); then
  echo "SUB2API_REPORT_PORT must be an integer from 1 to 65535." >&2
  exit 2
fi
app_port=$((10#$app_port))
if [[ ! -f $config_dir/environment ]]; then
  cat > "$config_dir/environment" <<EOF
ASPNETCORE_URLS=http://0.0.0.0:$app_port
ConnectionStrings__Database="Data Source=$data_dir/db/sub2api-report.db;Foreign Keys=True;Default Timeout=5;Pooling=True"
DataProtection__KeysPath=$data_dir/keys
Security__SecureCookies=false
DOTNET_EnableDiagnostics=0
EOF
  chmod 0640 "$config_dir/environment"
  chown root:"$service_user" "$config_dir/environment"
fi
if [[ -n $requested_port ]]; then
  if grep -q '^ASPNETCORE_URLS=' "$config_dir/environment"; then
    sed -i "s#^ASPNETCORE_URLS=.*#ASPNETCORE_URLS=http://0.0.0.0:$app_port#" \
      "$config_dir/environment"
  else
    printf 'ASPNETCORE_URLS=http://0.0.0.0:%s\n' "$app_port" >> "$config_dir/environment"
  fi
fi
if ! grep -q '^Security__SecureCookies=' "$config_dir/environment"; then
  printf '%s\n' 'Security__SecureCookies=false' >> "$config_dir/environment"
fi

wait_for_readiness() {
  for _ in $(seq 1 90); do
    if systemctl is-active --quiet "$service_name" \
      && curl --fail --silent "http://127.0.0.1:$app_port/health/ready" >/dev/null; then
      return
    fi
    sleep 2
  done
  return 1
}
old_target=$(readlink -f "$current_link" 2>/dev/null || true)
if [[ $old_target == "$release_dir" && -x $current_link/runtime/Sub2ApiReport.Api ]]; then
  systemctl daemon-reload
  systemctl enable --now "$service_name"
  echo "Waiting for application readiness on port $app_port (up to 180 seconds)..."
  wait_for_readiness || {
    echo "The service did not become ready on port $app_port." >&2
    exit 1
  }
  echo "Sub2API Report $version is already installed and running on port $app_port."
  exit 0
fi

echo "[2/5] Installing release files..."
staging_dir="$install_root/releases/.${version}.tmp"
rm -rf "$staging_dir"
install -d -m 0755 "$staging_dir"
cp -a "$bundle_dir/runtime" "$staging_dir/"
install -m 0644 "$bundle_dir/LICENSE" "$staging_dir/LICENSE"
install -m 0644 "$bundle_dir/CHANGELOG.md" "$staging_dir/CHANGELOG.md"
chown -R root:root "$staging_dir"
find "$staging_dir" -type d -exec chmod 0755 {} +
find "$staging_dir" -type f -exec chmod 0644 {} +
chmod 0755 "$staging_dir/runtime/Sub2ApiReport.Api" \
  "$staging_dir/runtime/Sub2ApiReport.Migrator" \
  "$staging_dir/runtime/Sub2ApiReport.Cli"
rm -rf "$release_dir"
mv "$staging_dir" "$release_dir"

echo "[3/5] Configuring systemd service..."
cat > "$systemd_dir/$service_name" <<EOF
[Unit]
Description=Sub2API Report
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=$service_user
Group=$service_user
WorkingDirectory=$current_link/runtime
Environment=ASPNETCORE_ENVIRONMENT=Production
EnvironmentFile=$config_dir/environment
ExecStartPre=$current_link/runtime/Sub2ApiReport.Migrator
ExecStart=$current_link/runtime/Sub2ApiReport.Api
Restart=always
RestartSec=5
TimeoutStartSec=180
TimeoutStopSec=60
UMask=0077
NoNewPrivileges=true
PrivateTmp=true
PrivateDevices=true
ProtectSystem=strict
ProtectHome=true
ProtectKernelTunables=true
ProtectKernelModules=true
ProtectControlGroups=true
RestrictSUIDSGID=true
ReadWritePaths=$data_dir

[Install]
WantedBy=multi-user.target
EOF

cat > "$control_path" <<EOF
#!/bin/sh
set -eu
export ConnectionStrings__Database='Data Source=$data_dir/db/sub2api-report.db;Foreign Keys=True;Default Timeout=5;Pooling=True'
export DataProtection__KeysPath='$data_dir/keys'
if [ "\$(id -u)" -eq 0 ] && command -v runuser >/dev/null 2>&1; then
  exec runuser -u '$service_user' -- '$current_link/runtime/Sub2ApiReport.Cli' "\$@"
fi
exec '$current_link/runtime/Sub2ApiReport.Cli' "\$@"
EOF
chmod 0755 "$control_path"
systemctl daemon-reload

backup_file=
rollback() {
  local exit_code=$?
  set +e
  systemctl stop "$service_name"
  if [[ -n $old_target && -d $old_target ]]; then
    ln -sfn "$old_target" "$current_link"
    if [[ -n $backup_file && -f $backup_file ]]; then
      rm -rf "$data_dir/db"
      tar -C "$data_dir" -xf "$backup_file"
      chown -R "$service_user:$service_user" "$data_dir/db"
    fi
    systemctl daemon-reload
    systemctl start "$service_name"
    echo "Installation failed; the previous release was restored." >&2
  else
    echo "Installation failed. Inspect: journalctl -u $service_name" >&2
  fi
  exit "$exit_code"
}
trap rollback ERR
echo "[4/5] Backing up data and switching release..."

if [[ -n $old_target ]]; then
  timestamp=$(date -u +%Y%m%dT%H%M%SZ)
  backup_file="$backup_root/pre-update-$timestamp.tar"
  systemctl stop "$service_name" || true
  tar -C "$data_dir" -cf "$backup_file" db
  chmod 0600 "$backup_file"
fi

ln -sfn "$release_dir" "$current_link"
systemctl enable "$service_name"
systemctl restart "$service_name"
echo "[5/5] Waiting for application readiness on port $app_port (up to 180 seconds)..."
wait_for_readiness || {
  echo "The systemd service did not become ready on port $app_port." >&2
  false
}

trap - ERR
find "$install_root/releases" -mindepth 1 -maxdepth 1 -type d -not -path "$release_dir" -mtime +30 -exec rm -rf {} +
printf 'Sub2API Report %s is running as systemd service %s.\n' "$version" "$service_name"
printf 'Open: http://<server>:%s\n' "$app_port"
printf 'Logs: sudo journalctl -u %s -f\n' "$service_name"
