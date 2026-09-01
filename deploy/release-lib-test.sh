#!/usr/bin/env bash
set -euo pipefail

repo_root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
test_root=$(mktemp -d /tmp/sub2api-report-release-lib-test.XXXXXX)
cleanup() {
  rm -rf "$test_root"
}
trap cleanup EXIT

create_image_archive() {
  local tag=$1
  local image_root="$test_root/app-image"
  local config target config_digest layer_digest target_digest
  mkdir -p "$image_root/blobs/sha256"
  config=$(jq -cn '{architecture:"amd64",os:"linux",config:{Labels:{"org.opencontainers.image.version":"1.2.0","io.sub2api-report.role":"app","io.sub2api-report.contract":"2"}},rootfs:{type:"layers",diff_ids:["sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"]}}')
  config_digest=$(printf '%s' "$config" | sha256sum | cut -d ' ' -f 1)
  printf '%s' "$config" > "$image_root/blobs/sha256/$config_digest"
  printf 'synthetic-app-layer' > "$image_root/layer.tar"
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
  tar -czf "$test_root/app.tar.gz" -C "$image_root" manifest.json index.json blobs
  printf 'sha256:%s\nsha256:%s\n' "$config_digest" "$target_digest" > "$test_root/app.digests"
}

create_image_archive sub2api-report-app:1.2.0
mapfile -t app_digests < "$test_root/app.digests"

# shellcheck source=deploy/release-lib.sh
source "$repo_root/deploy/release-lib.sh"
verify_image_archive_metadata "$test_root/app.tar.gz" \
  sub2api-report-app:1.2.0 "${app_digests[0]}" "${app_digests[1]}"
if verify_image_archive_metadata "$test_root/app.tar.gz" \
  sub2api-report-app:1.2.0 "${app_digests[0]}" \
  "sha256:$(printf '0%.0s' {1..64})" >/dev/null 2>&1; then
  echo "Archive metadata validation accepted the wrong target digest." >&2
  exit 1
fi

bundle="$test_root/bundle"
mkdir -p "$bundle/images"
cp "$test_root/app.tar.gz" "$bundle/images/sub2api-report-app-linux-amd64.tar.gz"
cp "$repo_root/deploy/upgrade-contract.json" "$bundle/upgrade-contract.json"
printf 'services: {}\n' > "$bundle/compose.yaml"
printf 'APP_PORT=8081\nBIND_ADDRESS=0.0.0.0\nSECURE_COOKIES=false\n' > "$bundle/.env.example"
printf 'synthetic changelog\n' > "$bundle/CHANGELOG.md"
printf 'synthetic license\n' > "$bundle/LICENSE"
printf '## [1.2.0]\n\nSynthetic release.\n' > "$bundle/RELEASE-NOTES.md"
(
  cd "$bundle/images"
  sha256sum sub2api-report-app-linux-amd64.tar.gz > checksums.txt
)
app_sha=$(sha256sum "$bundle/images/sub2api-report-app-linux-amd64.tar.gz" | cut -d ' ' -f 1)
app_size=$(stat -c '%s' "$bundle/images/sub2api-report-app-linux-amd64.tar.gz")
notes_sha=$(sha256sum "$bundle/RELEASE-NOTES.md" | cut -d ' ' -f 1)
notes_size=$(stat -c '%s' "$bundle/RELEASE-NOTES.md")
jq -n \
  --arg appSha "$app_sha" \
  --argjson appSize "$app_size" \
  --arg appId "${app_digests[0]}" \
  --arg appTarget "${app_digests[1]}" \
  --arg notesSha "$notes_sha" \
  --argjson notesSize "$notes_size" \
  '{schemaVersion:4,version:"1.2.0",channel:"stable",architecture:"linux/amd64",deploymentContractVersion:2,signatureAlgorithm:"RSASSA-PKCS1-v1_5-SHA256",app:{archiveSha256:$appSha,imageId:$appId,targetDigest:$appTarget,loadedTag:"sub2api-report-app:1.2.0",size:$appSize},database:{targetMigration:"synthetic",requiresBackupRestoreForRollback:true},releaseNotes:{sha256:$notesSha,size:$notesSize}}' \
  > "$bundle/release-manifest.json"
openssl genpkey -quiet -algorithm RSA -pkeyopt rsa_keygen_bits:2048 \
  -out "$test_root/signing-key.pem"
openssl pkey -in "$test_root/signing-key.pem" -pubout -out "$bundle/update-public-key.pem"
openssl dgst -sha256 -sign "$test_root/signing-key.pem" \
  -out "$bundle/release-manifest.sig" "$bundle/release-manifest.json"
verify_release_bundle "$bundle"

mock_bin="$test_root/bin"
mkdir -p "$mock_bin"
cat > "$mock_bin/docker" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
if [[ ${1:-} == compose ]]; then
  service=${*: -1}
  if [[ ${RESOLVE_CONTAINER_MODE:-single} == multiple ]]; then
    printf '%s-container\n%s-container-2\n' "$service" "$service"
  else
    printf '%s-container\n' "$service"
  fi
  exit 0
fi
if [[ ${1:-} == inspect && ${2:-} == --format && ${3:-} == *'.State.Status'* ]]; then
  case ${4:-} in
    app-container) printf 'running|healthy|\n' ;;
    app-container-2)
      if [[ ${RESOLVE_ORIGINAL_MODE:-single} == ambiguous ]]; then
        printf 'running|healthy|\n'
      else
        printf 'created|none|target-operation\n'
      fi
      ;;
    *) exit 1 ;;
  esac
  exit 0
fi
if [[ ${1:-} == inspect && ${2:-} == --format && ${3:-} == '{{.Image}}' ]]; then
  printf 'sha256:%064d\n' 1
  exit 0
fi
role=app
format=${*: -1}
case "$format" in
  '{{.Id}}')
    if [[ ${DOCKER_ID_MODE:-classic} == classic ]]; then
      sed -n '1p' "$RELEASE_LIB_TEST_ROOT/app.digests"
    elif [[ ${DOCKER_ID_MODE:-classic} == containerd ]]; then
      sed -n '2p' "$RELEASE_LIB_TEST_ROOT/app.digests"
    else
      printf 'sha256:%064d\n' 0
    fi
    ;;
  '{{.Os}}/{{.Architecture}}') printf 'linux/amd64\n' ;;
  *org.opencontainers.image.version*) printf '1.2.0\n' ;;
  *io.sub2api-report.role*) printf '%s\n' "$role" ;;
  *io.sub2api-report.contract*) printf '2\n' ;;
  *) echo "Unexpected docker inspect format: $format" >&2; exit 1 ;;
esac
EOF
chmod 0755 "$mock_bin/docker"
export PATH="$mock_bin:$PATH"
export RELEASE_LIB_TEST_ROOT="$test_root"
expected_container_image="sha256:$(printf '%064d' 1)"
[[ $(resolve_service_image_id /synthetic-install app) == "$expected_container_image" ]]
[[ $(RESOLVE_CONTAINER_MODE=multiple \
  resolve_service_image_id /synthetic-install app) == "$expected_container_image" ]]
if RESOLVE_CONTAINER_MODE=multiple RESOLVE_ORIGINAL_MODE=ambiguous \
  resolve_service_image_id /synthetic-install app >/dev/null 2>&1; then
  echo "Container resolution accepted an ambiguous candidate set." >&2
  exit 1
fi
DOCKER_ID_MODE=classic validate_loaded_images "$bundle"
DOCKER_ID_MODE=containerd validate_loaded_images "$bundle"
if DOCKER_ID_MODE=invalid validate_loaded_images "$bundle" >/dev/null 2>&1; then
  echo "Loaded image validation accepted an unsigned local ID." >&2
  exit 1
fi

env_dir="$test_root/env"
mkdir -p "$env_dir"
printf 'APP_PORT=9000\nBIND_ADDRESS=127.0.0.1\nSECURE_COOKIES=true\nINSTANCE_ID=legacy\nDOCKER_GID=999\n' > "$env_dir/.env"
write_instance_env "$env_dir"
grep -Fx 'APP_PORT=9000' "$env_dir/.env" >/dev/null
grep -Fx 'BIND_ADDRESS=127.0.0.1' "$env_dir/.env" >/dev/null
grep -Fx 'SECURE_COOKIES=true' "$env_dir/.env" >/dev/null
if grep -Eq '^(INSTANCE_ID|DOCKER_GID)=' "$env_dir/.env"; then
  echo "Legacy deployment-only environment values were retained." >&2
  exit 1
fi

echo "release library tests passed"
