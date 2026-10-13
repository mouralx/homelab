#!/usr/bin/env bash
set -euo pipefail
export COMPOSE_FILE=${COMPOSE_FILE:-services/compose.yaml}
images=$(docker compose --env-file .env config --format json | jq -er '[.services[].image | select(. != null)] | unique | .[]')
while IFS= read -r image; do
  printf 'Checking published image: %s\n' "$image"
  if ! docker manifest inspect "$image" >/dev/null; then
    printf 'Cannot access %s. Check the image tag and registry permissions; GH_PAT needs read:packages access to private GHCR images.\n' "$image" >&2
    exit 1
  fi
done <<< "$images"
