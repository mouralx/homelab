#!/usr/bin/env bash
set -euo pipefail
images=$(docker compose --env-file .env config --format json | jq -r '.services | to_entries[] | select(.key | startswith("domus-")) | .value.image | select(startswith("ghcr.io/"))')
while IFS= read -r image; do
  [[ -n "$image" ]] || continue
  echo "Checking $image"
  docker manifest inspect "$image" >/dev/null
done <<< "$images"
