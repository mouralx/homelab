#!/usr/bin/env bash
set -euo pipefail
export COMPOSE_FILE=${COMPOSE_FILE:-services/compose.yaml}
config=$(docker compose --env-file .env config --format json)
media_gid=$(jq -er '.services.digger.group_add[0]' <<< "$config")
[[ "$media_gid" =~ ^[0-9]+$ ]] || { echo 'DIGGER_MEDIA_GID must be numeric.' >&2; exit 1; }
storage_script="$PWD/scripts/digger-storage.sh:/setup/digger-storage.sh:ro"
# Container root respects the Docker host's UID mapping, including rootless Docker.
# Change only the two mount roots and known database/lock files, never the media tree.
docker compose --env-file .env run --rm --no-deps --user 0:0 --entrypoint sh \
  --volume "$storage_script" -e DIGGER_STORAGE_UID=1654 -e "DIGGER_STORAGE_GID=$media_gid" \
  digger /setup/digger-storage.sh prepare
# Use the image's normal non-root user and Compose supplementary groups.
docker compose --env-file .env run --rm --no-deps --entrypoint sh \
  --volume "$storage_script" digger /setup/digger-storage.sh check
