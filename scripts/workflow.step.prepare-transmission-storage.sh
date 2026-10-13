#!/usr/bin/env bash
set -euo pipefail
export COMPOSE_FILE=${COMPOSE_FILE:-services/compose.yaml}
config=$(docker compose --env-file .env config --format json)
image=$(jq -er '.services.transmission.image' <<< "$config")
uid=$(jq -er '.services.transmission.environment.PUID // "911"' <<< "$config")
gid=$(jq -er '.services.transmission.environment.PGID // "911"' <<< "$config")
[[ "$uid" =~ ^[0-9]+$ && "$gid" =~ ^[0-9]+$ ]] || { echo 'Transmission UID/GID must be numeric.' >&2; exit 1; }
mounts=(--volume "$PWD/scripts/transmission-storage.sh:/setup/transmission-storage.sh:ro")
for target in /config /downloads /watch; do
  source=$(jq -er --arg target "$target" '.services.transmission.volumes[] | select(.target == $target) | .source' <<< "$config")
  mounts+=(--volume "$source:$target")
done
# Stop the daemon so it cannot overwrite saved settings while access is restored.
docker compose --env-file .env stop transmission
docker run --rm --network none --user 0:0 --entrypoint sh "${mounts[@]}" \
  -e "TRANSMISSION_STORAGE_UID=$uid" -e "TRANSMISSION_STORAGE_GID=$gid" \
  "$image" /setup/transmission-storage.sh prepare
docker run --rm --network none --user "$uid:$gid" --entrypoint sh "${mounts[@]}" \
  -e "TRANSMISSION_STORAGE_UID=$uid" -e "TRANSMISSION_STORAGE_GID=$gid" \
  "$image" /setup/transmission-storage.sh check
