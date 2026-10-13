#!/usr/bin/env bash
set -euo pipefail
export COMPOSE_FILE=${COMPOSE_FILE:-services/compose.yaml}
config=$(docker compose --env-file .env config --format json)
media_gid=$(jq -er '.services.digger.group_add[0]' <<< "$config")
image=$(jq -er '.services.digger.image' <<< "$config")
data_directory=$(jq -er '.services.digger.volumes[] | select(.target == "/data") | .source' <<< "$config")
media_directory=$(jq -er '.services.digger.volumes[] | select(.target == "/movies") | .source' <<< "$config")
[[ "$media_gid" =~ ^[0-9]+$ ]] || { echo 'DIGGER_MEDIA_GID must be numeric.' >&2; exit 1; }
storage_script="$PWD/scripts/digger-storage.sh:/setup/digger-storage.sh:ro"
# Container root respects the Docker host's UID mapping, including rootless Docker.
# Change mount roots, known staging directories and database/lock files, not movie contents.
docker run --rm --network none --user 0:0 --entrypoint sh \
  --volume "$data_directory:/data" --volume "$media_directory:/movies" \
  --volume "$storage_script" -e DIGGER_STORAGE_UID=1654 -e "DIGGER_STORAGE_GID=$media_gid" \
  "$image" /setup/digger-storage.sh prepare
# Use the image's normal non-root user and Compose supplementary groups.
docker run --rm --network none --group-add "$media_gid" --entrypoint sh \
  --volume "$data_directory:/data" --volume "$media_directory:/movies" \
  --volume "$storage_script" "$image" /setup/digger-storage.sh check
