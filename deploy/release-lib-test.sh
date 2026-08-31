#!/usr/bin/env bash
set -euo pipefail

repo_root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
test_root=$(mktemp -d /tmp/sub2api-report-release-lib-test.XXXXXX)
cleanup() {
  rm -rf "$test_root"
}
trap cleanup EXIT

create_image_archive() {
  local role=$1
  local tag=$2
  local image_root="$test_root/$role-image"
  local config target config_digest layer_digest target_digest
  mkdir -p "$image_root/blobs/sha256"
  config=$(jq -cn --arg role "$role" '{architecture:"amd64",os:"linux",config:{Labels:{"org.opencontainers.image.version":"1.0.6","io.sub2api-report.role":$role}},rootfs:{type:"layers",diff_ids:["sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"]}}')
  config_digest=$(printf '%s' "$config" | sha256sum | cut -d ' ' -f 1)
  printf '%s' "$config" > "$image_root/blobs/sha256/$config_digest"
  printf 'synthetic-layer-%s' "$role" > "$image_root/layer.tar"
  layer_digest=$(sha256sum "$image_root/layer.tar" | cut -d ' ' -f 1)
  cp "$image_root/layer.tar" "$image_root/blobs/sha256/$layer_digest"
  target=$(jq -cn \
    --arg config "sha256:$config_digest" \
    --arg layer "sha256:$layer_digest" \
    '{schemaVersion:2,mediaType:"application/vnd.oci.image.manifest.v1+json",config:{mediaType:"application/vnd.oci.image.config.v1+json",digest:$config,size:1},layers:[{mediaType:"application/vnd.oci.image.layer.v1.tar",digest:$layer,size:1}]}')
  target_digest=$(printf '%s' "$target" | sha256sum | cut -d ' ' -f 1)
  printf '%s' "$target" > "$image_root/blobs/sha256/$target_digest"
  jq -cn --arg config "blobs/sha256/$config_digest" --arg tag "$tag" \
    --arg layer "blobs/sha256/$layer_digest" \
    '[{Config:$config,RepoTags:[$tag],Layers:[$layer]}]' > "$image_root/manifest.json"
  jq -cn --arg target "sha256:$target_digest" \
    '{schemaVersion:2,mediaType:"application/vnd.oci.image.index.v1+json",manifests:[{mediaType:"application/vnd.oci.image.manifest.v1+json",digest:$target,size:1}]}' > "$image_root/index.json"
  tar -czf "$test_root/$role.tar.gz" -C "$image_root" manifest.json index.json blobs
  printf 'sha256:%s\nsha256:%s\n' "$config_digest" "$target_digest" > "$test_root/$role.digests"
}

create_image_archive app sub2api-report-app:1.0.6
create_image_archive updater sub2api-report-updater:1.0.6
mapfile -t app_digests < "$test_root/app.digests"
mapfile -t updater_digests < "$test_root/updater.digests"

# shellcheck source=deploy/release-lib.sh
source "$repo_root/deploy/release-lib.sh"
validate_release_compatibility_file "$repo_root/deploy/release-compatibility.json" 1.1.2
validate_release_compatibility_file "$repo_root/deploy/release-compatibility.json" 1.1.2-internal.1

online_policy="$test_root/online-compatibility.json"
jq '.manualUpgradeRequired = false
  | .onlineInstallSupported = true
  | .onlineUpgradeFrom = ["1.1.1"]
  | .upgradeMessage = "支持从 v1.1.1 在线升级。"' \
  "$repo_root/deploy/release-compatibility.json" > "$online_policy"
validate_release_compatibility_file "$online_policy" 1.1.2

invalid_policy="$test_root/invalid-compatibility.json"
jq '.releaseVersion = "9.9.9"' "$online_policy" > "$invalid_policy"
if validate_release_compatibility_file "$invalid_policy" 1.1.2 >/dev/null 2>&1; then
  echo "Compatibility validation accepted a mismatched release version." >&2
  exit 1
fi

verify_image_archive_metadata "$test_root/app.tar.gz" \
  sub2api-report-app:1.0.6 "${app_digests[0]}" "${app_digests[1]}"
verify_image_archive_metadata "$test_root/updater.tar.gz" \
  sub2api-report-updater:1.0.6 "${updater_digests[0]}" "${updater_digests[1]}"
if verify_image_archive_metadata "$test_root/app.tar.gz" \
  sub2api-report-app:1.0.6 "${app_digests[0]}" "sha256:$(printf '0%.0s' {1..64})" >/dev/null 2>&1; then
  echo "Archive metadata validation accepted the wrong target digest." >&2
  exit 1
fi

jq -n \
  --arg appConfig "${app_digests[0]}" \
  --arg appTarget "${app_digests[1]}" \
  --arg updaterConfig "${updater_digests[0]}" \
  --arg updaterTarget "${updater_digests[1]}" \
  '{version:"1.0.6",app:{loadedTag:"sub2api-report-app:1.0.6",imageId:$appConfig,targetDigest:$appTarget},updater:{loadedTag:"sub2api-report-updater:1.0.6",imageId:$updaterConfig,targetDigest:$updaterTarget}}' \
  > "$test_root/release-manifest.json"

mock_bin="$test_root/bin"
mkdir -p "$mock_bin"
cat > "$mock_bin/docker" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
if [[ ${1:-} == compose ]]; then
  service=${*: -1}
  if [[ ${RESOLVE_CONTAINER_MODE:-single} == multiple && $service == app ]]; then
    printf 'app-container\ntarget-app-container\n'
  else
    printf '%s-container\n' "$service"
  fi
  exit 0
fi
if [[ ${1:-} == inspect && ${2:-} == --format && ${3:-} == '{{.Image}}' ]]; then
  case ${4:-} in
    app-container) printf 'sha256:%064d\n' 1 ;;
    updater-container) printf 'sha256:%064d\n' 2 ;;
    target-app-container) printf 'sha256:%064d\n' 3 ;;
    *) exit 1 ;;
  esac
  exit 0
fi
if [[ ${1:-} == inspect && ${2:-} == --format && ${3:-} == *'.State.Status'* ]]; then
  case ${4:-} in
    app-container)
      if [[ ${RESOLVE_ORIGINAL_MODE:-original} == upgraded ]]; then
        printf 'running|healthy|previous-operation\n'
      else
        printf 'running|healthy|\n'
      fi
      ;;
    updater-container) printf 'running|healthy|\n' ;;
    target-app-container) printf 'created|none|target-operation\n' ;;
    *) exit 1 ;;
  esac
  exit 0
fi
role=app
[[ $* == *sub2api-report-updater* ]] && role=updater
format=${*: -1}
case "$format" in
  '{{.Id}}')
    case ${DOCKER_ID_MODE:-classic} in
      classic) sed -n '1p' "$RELEASE_LIB_TEST_ROOT/$role.digests" ;;
      containerd) sed -n '2p' "$RELEASE_LIB_TEST_ROOT/$role.digests" ;;
      *) printf 'sha256:%064d\n' 0 ;;
    esac
    ;;
  '{{.Os}}/{{.Architecture}}')
    printf 'linux/amd64\n'
    ;;
  *org.opencontainers.image.version*)
    printf '1.0.6\n'
    ;;
  *io.sub2api-report.role*)
    printf '%s\n' "$role"
    ;;
  *)
    echo "Unexpected docker inspect format: $format" >&2
    exit 1
    ;;
esac
EOF
chmod 0755 "$mock_bin/docker"
export PATH="$mock_bin:$PATH"
export RELEASE_LIB_TEST_ROOT="$test_root"

expected_app_container_image="sha256:$(printf '%064d' 1)"
expected_updater_container_image="sha256:$(printf '%064d' 2)"
[[ $(resolve_service_image_id /synthetic-install app) == "$expected_app_container_image" ]]
[[ $(resolve_service_image_id /synthetic-install updater) == "$expected_updater_container_image" ]]
[[ $(RESOLVE_CONTAINER_MODE=multiple \
  resolve_service_image_id /synthetic-install app) == "$expected_app_container_image" ]]
[[ $(RESOLVE_CONTAINER_MODE=multiple RESOLVE_ORIGINAL_MODE=upgraded \
  resolve_service_image_id /synthetic-install app) == "$expected_app_container_image" ]]
grep -F "old_app_id=\$(resolve_service_image_id \"\$install_dir\" app)" \
  "$repo_root/deploy/update.sh" >/dev/null
grep -F "old_updater_id=\$(resolve_service_image_id \"\$install_dir\" updater)" \
  "$repo_root/deploy/update.sh" >/dev/null

DOCKER_ID_MODE=classic validate_loaded_images "$test_root"
DOCKER_ID_MODE=containerd validate_loaded_images "$test_root"
if DOCKER_ID_MODE=invalid validate_loaded_images "$test_root" >/dev/null 2>&1; then
  echo "Loaded image validation accepted an unsigned local ID." >&2
  exit 1
fi

echo "release library tests passed"
