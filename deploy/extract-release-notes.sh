#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 3 ]]; then
  echo "Usage: $0 <changelog-file> <version-without-v> <output-file>" >&2
  exit 2
fi

changelog_file=$1
version=$2
output_file=$3

if [[ ! -f $changelog_file ]]; then
  echo "Changelog not found: $changelog_file" >&2
  exit 2
fi
if [[ ! $version =~ ^[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.-]+)?$ ]]; then
  echo "Invalid version: $version" >&2
  exit 2
fi

section_heading="## [$version]"
temporary_file=$(mktemp)
trap 'rm -f "$temporary_file"' EXIT

awk -v heading="$section_heading" '
  index($0, heading) == 1 {
    if (found) {
      exit
    }
    found = 1
  }
  found && /^## \[/ && index($0, heading) != 1 {
    exit
  }
  found && /^\[[^]]+\]:/ {
    exit
  }
  found {
    print
  }
  END {
    if (!found) {
      exit 3
    }
  }
' "$changelog_file" > "$temporary_file" || {
  echo "CHANGELOG.md has no release section for $version." >&2
  exit 1
}

if ! grep -Eq '^### (Added|Changed|Deprecated|Removed|Fixed|Security)$' "$temporary_file" \
  || ! grep -Eq '^- .+' "$temporary_file"; then
  echo "The $version changelog section must contain a supported category and at least one item." >&2
  exit 1
fi

install -D -m 0644 "$temporary_file" "$output_file"
