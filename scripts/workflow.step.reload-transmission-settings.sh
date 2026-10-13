#!/usr/bin/env bash
set -euo pipefail
# SIGHUP makes the running daemon read the effective settings after startup.
daemon_uid=$(docker inspect --format '{{json .Config.Env}}' transmission | jq -er 'map(select(startswith("PUID="))) | (.[0] // "PUID=911") | split("=")[1]')
[[ "$daemon_uid" =~ ^[0-9]+$ ]] || { echo 'Transmission UID must be numeric.' >&2; exit 1; }
docker exec --user "$daemon_uid" transmission sh -c 'pidof transmission-daemon | xargs -r kill -HUP'
echo 'Reloaded the running Transmission daemon settings.'
