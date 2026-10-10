#!/usr/bin/env bash
set -euo pipefail
# Inspect only the existing version marker; never initialize, move or upgrade data.
config=${1:?Resolved Compose JSON file is required}
source=$(jq -er '.services.postgres.volumes[] | select(.target == "/var/lib/postgresql/data") | .source' "$config")
expected=$(jq -er '.services.postgres.labels["io.homelab.postgres.major"] // "15"' "$config")
image=$(jq -er '.services.postgres.image' "$config")
if [[ ! "$expected" =~ ^[1-9][0-9]*$ ]]; then
  echo 'POSTGRES_MAJOR must be a positive integer.' >&2; exit 1
fi
if [[ "$image" =~ :pg([0-9]+)(-|$) || "$image" =~ :([0-9]+)(-|$) ]]; then
  image_major=${BASH_REMATCH[1]}
  if [[ "$image_major" != "$expected" ]]; then
    echo "PostgreSQL image major $image_major differs from POSTGRES_MAJOR $expected." >&2; exit 1
  fi
fi
if [[ -f "$source/PG_VERSION" ]]; then
  actual=$(< "$source/PG_VERSION")
  if [[ "$actual" != "$expected" ]]; then
    echo "Existing PostgreSQL data is version $actual, but POSTGRES_MAJOR is $expected. Match POSTGRES_IMAGE/POSTGRES_MAJOR to the existing server; no automatic upgrade is performed." >&2
    exit 1
  fi
fi
