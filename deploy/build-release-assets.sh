#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "Usage: $0 <version-without-v> <output-directory>" >&2
  exit 2
fi

version=$1
output_dir=$2
repo_root=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
revision=${GITHUB_SHA:-$(git -C "$repo_root" rev-parse HEAD)}
repository=${GITHUB_REPOSITORY:-example/sub2api-report}
contract_version=1
manual_upgrade_required=${MANUAL_UPGRADE_REQUIRED:-false}
online_install_supported=${ONLINE_INSTALL_SUPPORTED:-true}
minimum_updater_version=${MINIMUM_UPDATER_VERSION:-1.0.0}
if [[ ! $minimum_updater_version =~ ^[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.-]+)?$ ]]; then
  echo "MINIMUM_UPDATER_VERSION must be a valid SemVer." >&2
  exit 2
fi
for boolean_value in "$manual_upgrade_required" "$online_install_supported"; do
  [[ $boolean_value == true || $boolean_value == false ]] || {
    echo "Release compatibility flags must be true or false." >&2
    exit 2
  }
done

if [[ ! $version =~ ^[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.-]+)?$ ]]; then
  echo "Invalid version: $version" >&2
  exit 2
fi
if [[ -z ${RELEASE_SIGNING_KEY_FILE:-} || ! -f ${RELEASE_SIGNING_KEY_FILE:-} ]]; then
  echo "RELEASE_SIGNING_KEY_FILE must reference the release RSA private key." >&2
  exit 2
fi

for command_name in awk docker gzip install jq openssl sed sha256sum tar; do
  command -v "$command_name" >/dev/null 2>&1 || {
    echo "$command_name is required." >&2
    exit 2
  }
done

docker buildx version >/dev/null 2>&1 || {
  echo "Docker Buildx is required." >&2
  exit 2
}

rm -rf "$output_dir"
mkdir -p "$output_dir"
output_dir=$(cd "$output_dir" && pwd)
release_notes_asset="release-notes-v${version}.md"
release_notes_section=${RELEASE_NOTES_SECTION:-$version}
"$repo_root/deploy/extract-release-notes.sh" \
  "$repo_root/CHANGELOG.md" "$release_notes_section" "$output_dir/$release_notes_asset"
if [[ $release_notes_section == Unreleased ]]; then
  sed -i "1s/^## \[Unreleased\]$/## [$version] - candidate/" "$output_dir/$release_notes_asset"
fi
install -m 0644 "$repo_root/CHANGELOG.md" "$output_dir/CHANGELOG.md"
install -m 0644 "$repo_root/LICENSE" "$output_dir/LICENSE"
work_dir=$(mktemp -d)
trap 'rm -rf "$work_dir"' EXIT

source_url="https://github.com/${repository}"
app_version_tag="sub2api-report-app:${version}"
app_current_tag="sub2api-report-app:current"
updater_version_tag="sub2api-report-updater:${version}"
updater_bootstrap_tag="sub2api-report-updater:bootstrap"

build_image() {
  local dockerfile=$1
  shift
  docker buildx build \
    --platform linux/amd64 \
    --load \
    --file "$repo_root/$dockerfile" \
    --build-arg "VERSION=$version" \
    --build-arg "REVISION=$revision" \
    --build-arg "SOURCE_URL=$source_url" \
    "$@" \
    "$repo_root"
}

build_image Dockerfile --tag "$app_version_tag" --tag "$app_current_tag"
build_image Dockerfile.updater --tag "$updater_version_tag" --tag "$updater_bootstrap_tag"

validate_image() {
  local image=$1
  local expected_role=$2
  local actual_os actual_arch actual_version actual_role
  actual_os=$(docker image inspect "$image" --format '{{.Os}}')
  actual_arch=$(docker image inspect "$image" --format '{{.Architecture}}')
  actual_version=$(docker image inspect "$image" --format '{{index .Config.Labels "org.opencontainers.image.version"}}')
  actual_role=$(docker image inspect "$image" --format '{{index .Config.Labels "io.sub2api-report.role"}}')
  [[ $actual_os == linux && $actual_arch == amd64 ]] || {
    echo "$image has unexpected platform $actual_os/$actual_arch" >&2
    exit 1
  }
  [[ $actual_version == "$version" && $actual_role == "$expected_role" ]] || {
    echo "$image has unexpected release labels" >&2
    exit 1
  }
}

validate_image "$app_version_tag" app
validate_image "$updater_version_tag" updater

app_asset="sub2api-report-app-v${version}-linux-amd64.tar.gz"
updater_asset="sub2api-report-updater-v${version}-linux-amd64.tar.gz"
bundle_asset="sub2api-report-v${version}-linux-amd64.tar.gz"

docker save "$app_version_tag" | gzip -n -9 > "$output_dir/$app_asset"
docker save "$updater_version_tag" | gzip -n -9 > "$output_dir/$updater_asset"

app_sha=$(sha256sum "$output_dir/$app_asset" | awk '{print $1}')
updater_sha=$(sha256sum "$output_dir/$updater_asset" | awk '{print $1}')
app_size=$(stat -c '%s' "$output_dir/$app_asset")
updater_size=$(stat -c '%s' "$output_dir/$updater_asset")
release_notes_sha=$(sha256sum "$output_dir/$release_notes_asset" | awk '{print $1}')
release_notes_size=$(stat -c '%s' "$output_dir/$release_notes_asset")
app_image_id=$(docker image inspect "$app_version_tag" --format '{{.Id}}')
updater_image_id=$(docker image inspect "$updater_version_tag" --format '{{.Id}}')
target_migration=$(find "$repo_root/src/Sub2ApiReport.Infrastructure/Persistence/Migrations" \
  -maxdepth 1 -type f -name '[0-9]*.cs' ! -name '*.Designer.cs' -printf '%f\n' \
  | sort | tail -n 1 | sed 's/\.cs$//')
published_at=$(date -u +'%Y-%m-%dT%H:%M:%SZ')

jq -n \
  --arg version "$version" \
  --arg minimumUpdaterVersion "$minimum_updater_version" \
  --arg publishedAt "$published_at" \
  --arg repository "$repository" \
  --arg appAsset "$app_asset" \
  --arg appSha "$app_sha" \
  --arg appImageId "$app_image_id" \
  --arg updaterAsset "$updater_asset" \
  --arg updaterSha "$updater_sha" \
  --arg updaterImageId "$updater_image_id" \
  --arg releaseNotesAsset "$release_notes_asset" \
  --arg releaseNotesSha "$release_notes_sha" \
  --arg targetMigration "$target_migration" \
  --argjson appSize "$app_size" \
  --argjson updaterSize "$updater_size" \
  --argjson releaseNotesSize "$release_notes_size" \
  --argjson manualUpgradeRequired "$manual_upgrade_required" \
  --argjson onlineInstallSupported "$online_install_supported" \
  --argjson contractVersion "$contract_version" \
  '{
    schemaVersion: 1,
    version: $version,
    channel: "stable",
    publishedAt: $publishedAt,
    architecture: "linux/amd64",
    deploymentContractVersion: $contractVersion,
    minimumUpdaterVersion: $minimumUpdaterVersion,
    manualUpgradeRequired: $manualUpgradeRequired,
    onlineInstallSupported: $onlineInstallSupported,
    signatureAlgorithm: "RSASSA-PKCS1-v1_5-SHA256",
    app: {
      archiveUrl: ("https://github.com/" + $repository + "/releases/download/v" + $version + "/" + $appAsset),
      archiveSha256: $appSha,
      imageId: $appImageId,
      loadedTag: ("sub2api-report-app:" + $version),
      size: $appSize
    },
    updater: {
      archiveUrl: ("https://github.com/" + $repository + "/releases/download/v" + $version + "/" + $updaterAsset),
      archiveSha256: $updaterSha,
      imageId: $updaterImageId,
      loadedTag: ("sub2api-report-updater:" + $version),
      size: $updaterSize,
      selfUpdateSupported: false
    },
    database: {
      targetMigration: $targetMigration,
      requiresBackupRestoreForRollback: true
    },
    releaseNotes: {
      pageUrl: ("https://github.com/" + $repository + "/releases/tag/v" + $version),
      assetUrl: ("https://github.com/" + $repository + "/releases/download/v" + $version + "/" + $releaseNotesAsset),
      sha256: $releaseNotesSha,
      size: $releaseNotesSize
    }
  }' > "$output_dir/release-manifest.json"

openssl rsa -in "$RELEASE_SIGNING_KEY_FILE" -check -noout >/dev/null
openssl dgst -sha256 -sign "$RELEASE_SIGNING_KEY_FILE" \
  -out "$output_dir/release-manifest.sig" "$output_dir/release-manifest.json"
openssl pkey -in "$RELEASE_SIGNING_KEY_FILE" -pubout \
  -out "$output_dir/update-public-key.pem"

bundle_dir="$work_dir/bundle"
mkdir -p "$bundle_dir/images"
install -m 0644 "$repo_root/deploy/compose.yaml" "$bundle_dir/compose.yaml"
install -m 0644 "$repo_root/deploy/.env.example" "$bundle_dir/.env.example"
install -m 0644 "$repo_root/deploy/upgrade-contract.json" "$bundle_dir/upgrade-contract.json"
install -m 0644 "$output_dir/CHANGELOG.md" "$bundle_dir/CHANGELOG.md"
install -m 0644 "$output_dir/LICENSE" "$bundle_dir/LICENSE"
install -m 0644 "$output_dir/$release_notes_asset" "$bundle_dir/RELEASE-NOTES.md"
install -m 0755 "$repo_root/deploy/release-lib.sh" "$bundle_dir/release-lib.sh"
install -m 0755 "$repo_root/deploy/install.sh" "$bundle_dir/install.sh"
install -m 0755 "$repo_root/deploy/update.sh" "$bundle_dir/update.sh"
install -m 0755 "$repo_root/deploy/appctl" "$bundle_dir/appctl"
install -m 0644 "$output_dir/release-manifest.json" "$bundle_dir/release-manifest.json"
install -m 0644 "$output_dir/release-manifest.sig" "$bundle_dir/release-manifest.sig"
install -m 0644 "$output_dir/update-public-key.pem" "$bundle_dir/update-public-key.pem"
install -m 0644 "$output_dir/$app_asset" "$bundle_dir/images/sub2api-report-app-linux-amd64.tar.gz"
install -m 0644 "$output_dir/$updater_asset" "$bundle_dir/images/sub2api-report-updater-linux-amd64.tar.gz"
(
  cd "$bundle_dir/images"
  sha256sum sub2api-report-app-linux-amd64.tar.gz \
    sub2api-report-updater-linux-amd64.tar.gz > checksums.txt
)

tar --sort=name --mtime='UTC 1970-01-01' --owner=0 --group=0 --numeric-owner \
  -C "$bundle_dir" -cf - . | gzip -n -9 > "$output_dir/$bundle_asset"

(
  cd "$output_dir"
  sha256sum "$app_asset" "$updater_asset" "$bundle_asset" \
    release-manifest.json release-manifest.sig update-public-key.pem \
    CHANGELOG.md LICENSE "$release_notes_asset" > checksums.txt
)

printf 'Release assets written to %s\n' "$output_dir"
