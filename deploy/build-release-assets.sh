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
compatibility_file="$repo_root/deploy/release-compatibility.json"
# shellcheck source=deploy/release-lib.sh
source "$repo_root/deploy/release-lib.sh"

if [[ ! $version =~ ^[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.-]+)?$ ]]; then
  echo "Invalid version: $version" >&2
  exit 2
fi
if [[ -z ${RELEASE_SIGNING_KEY_FILE:-} || ! -f ${RELEASE_SIGNING_KEY_FILE:-} ]]; then
  echo "RELEASE_SIGNING_KEY_FILE must reference the release RSA private key." >&2
  exit 2
fi

for command_name in awk docker dotnet gzip install jq openssl sed sha256sum sort tar; do
  command -v "$command_name" >/dev/null 2>&1 || {
    echo "$command_name is required." >&2
    exit 2
  }
done

validate_release_compatibility_file "$compatibility_file" "$version" || exit 2

manifest_schema_version=$(jq -r '.manifestSchemaVersion' "$compatibility_file")
contract_version=$(jq -r '.deploymentContractVersion' "$compatibility_file")
minimum_updater_version=$(jq -r '.minimumUpdaterVersion' "$compatibility_file")
manual_upgrade_required=$(jq -r '.manualUpgradeRequired' "$compatibility_file")
online_install_supported=$(jq -r '.onlineInstallSupported' "$compatibility_file")
online_upgrade_from=$(jq -c '.onlineUpgradeFrom' "$compatibility_file")
upgrade_message=$(jq -r '.upgradeMessage' "$compatibility_file")

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
if [[ $release_notes_section != "$version" ]]; then
  sed -i -E "1s/^## \[[^]]+\]( - .*)?$/## [$version] - candidate/" \
    "$output_dir/$release_notes_asset"
fi
install -m 0644 "$repo_root/CHANGELOG.md" "$output_dir/CHANGELOG.md"
install -m 0644 "$compatibility_file" "$output_dir/release-compatibility.json"
install -m 0644 "$repo_root/LICENSE" "$output_dir/LICENSE"
work_dir=$(mktemp -d)
trap 'rm -rf "$work_dir"' EXIT

server_asset="sub2api-report-server-v${version}-linux-amd64.tar.gz"
server_dir="$work_dir/server"
mkdir -p "$server_dir/runtime"
publish_server_project() {
  local project=$1
  dotnet publish "$repo_root/$project" \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained true \
    --output "$server_dir/runtime" \
    /p:UseAppHost=true \
    /p:PublishSingleFile=false \
    /p:PublishTrimmed=false \
    /p:DebugType=None \
    /p:Version="$version" \
    /p:SourceRevisionId="$revision" \
    /p:ContinuousIntegrationBuild=true
}
publish_server_project src/Sub2ApiReport.Api/Sub2ApiReport.Api.csproj
publish_server_project src/Sub2ApiReport.Migrator/Sub2ApiReport.Migrator.csproj
publish_server_project src/Sub2ApiReport.Cli/Sub2ApiReport.Cli.csproj
find "$server_dir" -type f -name '*.pdb' -delete
install -m 0755 "$repo_root/deploy/server-install.sh" "$server_dir/server-install.sh"
install -m 0644 "$repo_root/LICENSE" "$server_dir/LICENSE"
install -m 0644 "$repo_root/CHANGELOG.md" "$server_dir/CHANGELOG.md"
printf '%s\n' "$version" > "$server_dir/VERSION"
tar --sort=name --mtime='UTC 1970-01-01' --owner=0 --group=0 --numeric-owner \
  -C "$server_dir" -cf - . | gzip -n -9 > "$output_dir/$server_asset"

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

inspect_saved_image_archive() {
  local archive=$1
  local expected_tag=$2
  local docker_manifest index_json config_path config_hex config_digest target_digest target_hex
  docker_manifest=$(tar -xOzf "$archive" manifest.json)
  jq -e --arg tag "$expected_tag" \
    'length == 1 and .[0].RepoTags == [$tag] and (.[0].Layers | length > 0)' \
    <<<"$docker_manifest" >/dev/null || {
    echo "$archive does not contain exactly the expected image tag." >&2
    return 1
  }
  config_path=$(jq -r '.[0].Config' <<<"$docker_manifest")
  [[ $config_path =~ ^(blobs/sha256/)?[a-f0-9]{64}(\.json)?$ ]] || {
    echo "$archive contains an invalid image config path." >&2
    return 1
  }
  config_hex=${config_path#blobs/sha256/}
  config_hex=${config_hex%.json}
  config_digest="sha256:$(tar -xOzf "$archive" "$config_path" | sha256sum | awk '{print $1}')"
  [[ $config_digest == "sha256:$config_hex" ]] || {
    echo "$archive image config content does not match its digest path." >&2
    return 1
  }
  index_json=$(tar -xOzf "$archive" index.json)
  jq -e '(.manifests | length) == 1' <<<"$index_json" >/dev/null || {
    echo "$archive index must contain exactly one target descriptor." >&2
    return 1
  }
  target_digest=$(jq -r '.manifests[0].digest' <<<"$index_json")
  [[ $target_digest =~ ^sha256:[a-f0-9]{64}$ ]] || {
    echo "$archive contains an invalid target digest." >&2
    return 1
  }
  target_hex=${target_digest#sha256:}
  [[ $(tar -xOzf "$archive" "blobs/sha256/$target_hex" | sha256sum | awk '{print $1}') == "$target_hex" ]] || {
    echo "$archive target descriptor content does not match its digest." >&2
    return 1
  }
  printf '%s\t%s\n' "$config_digest" "$target_digest"
}

IFS=$'\t' read -r app_image_id app_target_digest < <(
  inspect_saved_image_archive "$output_dir/$app_asset" "$app_version_tag"
)
IFS=$'\t' read -r updater_image_id updater_target_digest < <(
  inspect_saved_image_archive "$output_dir/$updater_asset" "$updater_version_tag"
)

app_sha=$(sha256sum "$output_dir/$app_asset" | awk '{print $1}')
updater_sha=$(sha256sum "$output_dir/$updater_asset" | awk '{print $1}')
app_size=$(stat -c '%s' "$output_dir/$app_asset")
updater_size=$(stat -c '%s' "$output_dir/$updater_asset")
release_notes_sha=$(sha256sum "$output_dir/$release_notes_asset" | awk '{print $1}')
release_notes_size=$(stat -c '%s' "$output_dir/$release_notes_asset")
target_migration=$(find "$repo_root/src/Sub2ApiReport.Infrastructure/Persistence/Migrations" \
  -maxdepth 1 -type f -name '[0-9]*.cs' ! -name '*.Designer.cs' -printf '%f\n' \
  | sort | tail -n 1 | sed 's/\.cs$//')
published_at=$(date -u +'%Y-%m-%dT%H:%M:%SZ')

jq -n \
  --arg version "$version" \
  --arg minimumUpdaterVersion "$minimum_updater_version" \
  --arg upgradeMessage "$upgrade_message" \
  --argjson onlineUpgradeFrom "$online_upgrade_from" \
  --argjson manifestSchemaVersion "$manifest_schema_version" \
  --arg publishedAt "$published_at" \
  --arg repository "$repository" \
  --arg appAsset "$app_asset" \
  --arg appSha "$app_sha" \
  --arg appImageId "$app_image_id" \
  --arg appTargetDigest "$app_target_digest" \
  --arg updaterAsset "$updater_asset" \
  --arg updaterSha "$updater_sha" \
  --arg updaterImageId "$updater_image_id" \
  --arg updaterTargetDigest "$updater_target_digest" \
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
    schemaVersion: $manifestSchemaVersion,
    version: $version,
    channel: "stable",
    publishedAt: $publishedAt,
    architecture: "linux/amd64",
    deploymentContractVersion: $contractVersion,
    minimumUpdaterVersion: $minimumUpdaterVersion,
    manualUpgradeRequired: $manualUpgradeRequired,
    onlineInstallSupported: $onlineInstallSupported,
    onlineUpgradeFrom: $onlineUpgradeFrom,
    upgradeMessage: $upgradeMessage,
    signatureAlgorithm: "RSASSA-PKCS1-v1_5-SHA256",
    app: {
      archiveUrl: ("https://github.com/" + $repository + "/releases/download/v" + $version + "/" + $appAsset),
      archiveSha256: $appSha,
      imageId: $appImageId,
      targetDigest: $appTargetDigest,
      loadedTag: ("sub2api-report-app:" + $version),
      size: $appSize
    },
    updater: {
      archiveUrl: ("https://github.com/" + $repository + "/releases/download/v" + $version + "/" + $updaterAsset),
      archiveSha256: $updaterSha,
      imageId: $updaterImageId,
      targetDigest: $updaterTargetDigest,
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
install -m 0644 "$output_dir/release-compatibility.json" "$bundle_dir/release-compatibility.json"
install -m 0644 "$output_dir/CHANGELOG.md" "$bundle_dir/CHANGELOG.md"
install -m 0644 "$output_dir/LICENSE" "$bundle_dir/LICENSE"
install -m 0644 "$output_dir/$release_notes_asset" "$bundle_dir/RELEASE-NOTES.md"
install -m 0755 "$repo_root/deploy/bootstrap.sh" "$bundle_dir/bootstrap.sh"
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
  sha256sum "$app_asset" "$updater_asset" "$bundle_asset" "$server_asset" \
    release-manifest.json release-manifest.sig release-compatibility.json update-public-key.pem \
    CHANGELOG.md LICENSE "$release_notes_asset" > checksums.txt
)

printf 'Release assets written to %s\n' "$output_dir"
