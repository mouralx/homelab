#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
fixture=$(mktemp -d)
trap 'rm -rf "$fixture"' EXIT
mkdir "$fixture/data"
printf '15\n' > "$fixture/data/PG_VERSION"
config() {
  jq -n --arg source "$fixture/data" --arg major "$1" --arg image "$2" \
    '{services: {postgres: {image: $image, labels: {"io.homelab.postgres.major": $major}, volumes: [{source: $source, target: "/var/lib/postgresql/data"}]}}}' > "$fixture/config.json"
}
config 15 pgvector/pgvector:pg15
bash scripts/validate-postgres-storage.sh "$fixture/config.json"
config 18 postgres:18-bookworm
if bash scripts/validate-postgres-storage.sh "$fixture/config.json" >/dev/null 2>&1; then echo 'Accepted mismatched existing data' >&2; exit 1; fi
config 15 postgres:18-bookworm
if bash scripts/validate-postgres-storage.sh "$fixture/config.json" >/dev/null 2>&1; then echo 'Accepted mismatched image' >&2; exit 1; fi
config bad pgvector/pgvector:pg15
if bash scripts/validate-postgres-storage.sh "$fixture/config.json" >/dev/null 2>&1; then echo 'Accepted invalid major' >&2; exit 1; fi
rm "$fixture/data/PG_VERSION"
config 15 pgvector/pgvector:pg15
bash scripts/validate-postgres-storage.sh "$fixture/config.json"
echo 'All 5 PostgreSQL storage checks passed.'
