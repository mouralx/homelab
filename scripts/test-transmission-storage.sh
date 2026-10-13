#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
fixture=$(mktemp -d)
suffix=$(basename "$fixture" | tr '[:upper:].' '[:lower:]-')
volumes=("homelab-transmission-config-$suffix" "homelab-transmission-downloads-$suffix" "homelab-transmission-watch-$suffix")
cleanup() { docker volume rm "${volumes[@]}" >/dev/null; rmdir "$fixture"; }
for volume in "${volumes[@]}"; do docker volume create "$volume" >/dev/null; done
trap cleanup EXIT
mounts=(-v "${volumes[0]}:/config" -v "${volumes[1]}:/downloads" -v "${volumes[2]}:/watch" -v "$PWD/scripts/transmission-storage.sh:/setup.sh:ro")
settings=(-e TRANSMISSION_STORAGE_UID=911 -e TRANSMISSION_STORAGE_GID=911)
docker run --rm --network none "${mounts[@]}" busybox:1.37 sh -c '
  mkdir /config/blocklists
  printf "original-settings" > /config/settings.json
  printf "original-state" > /config/dht.dat
  printf "original-blocklist" > /config/blocklists/list
  printf "unrelated-media" > /downloads/unrelated.mkv
  chmod 700 /config /config/blocklists
  chmod 600 /config/settings.json /config/dht.dat /config/blocklists/list /downloads/unrelated.mkv
'
if docker run --rm --network none --user 911:911 "${mounts[@]}" "${settings[@]}" busybox:1.37 sh /setup.sh check >/dev/null 2>&1; then
  echo 'Accepted unreadable settings' >&2; exit 1
fi
docker run --rm --network none "${mounts[@]}" "${settings[@]}" busybox:1.37 sh /setup.sh prepare
docker run --rm --network none --user 911:911 "${mounts[@]}" "${settings[@]}" busybox:1.37 sh /setup.sh check
docker run --rm --network none --user 911:911 "${mounts[@]}" busybox:1.37 sh -c '
  test "$(cat /config/settings.json)" = original-settings
  test "$(cat /config/settings.json.permissions-backup)" = original-settings
  test "$(cat /config/dht.dat)" = original-state
  test "$(cat /config/blocklists/list)" = original-blocklist
  test -w /config/blocklists/list
'
docker run --rm --network none "${mounts[@]}" busybox:1.37 sh -c 'test "$(stat -c "%u %g %a" /downloads/unrelated.mkv)" = "0 0 600"'
echo 'Transmission storage checks passed: saved configuration/state preserved, ownership repaired, missing download directories created and media untouched.'
