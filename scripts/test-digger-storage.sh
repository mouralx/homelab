#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
fixture=$(mktemp -d)
data_volume="homelab-digger-data-$(basename "$fixture" | tr '[:upper:].' '[:lower:]-')"
media_volume="homelab-digger-media-$(basename "$fixture" | tr '[:upper:].' '[:lower:]-')"
cleanup() {
  docker volume rm "$data_volume" "$media_volume" >/dev/null
  rmdir "$fixture"
}
docker volume create "$data_volume" >/dev/null
docker volume create "$media_volume" >/dev/null
trap cleanup EXIT
mounts=(-v "$data_volume:/data" -v "$media_volume:/movies" -v "$PWD/scripts/digger-storage.sh:/setup.sh:ro")
docker run --rm "${mounts[@]}" busybox:1.37 sh -c '
  chmod 755 /data /movies
  printf database > /data/digger.db
  printf lock > /movies/.digger.lock
  printf existing-movie > /movies/unrelated.mkv
  chmod 644 /data/digger.db /movies/.digger.lock /movies/unrelated.mkv
  stat -c "%i %u %g %a" /movies/.digger.lock > /data/lock-before
  stat -c "%u %g %a" /movies/unrelated.mkv > /data/movie-before
'
if docker run --rm --user 1654:1654 --group-add 911 "${mounts[@]}" busybox:1.37 sh /setup.sh check >/dev/null 2>&1; then
  echo 'Accepted unwritable storage' >&2; exit 1
fi
docker run --rm -e DIGGER_STORAGE_UID=1654 -e DIGGER_STORAGE_GID=911 "${mounts[@]}" busybox:1.37 sh /setup.sh prepare
docker run --rm --user 1654:1654 --group-add 911 "${mounts[@]}" busybox:1.37 sh /setup.sh check
docker run --rm --user 911:911 "${mounts[@]}" busybox:1.37 sh -c 'touch /movies/transmission-check; rm /movies/transmission-check'
docker run --rm "${mounts[@]}" busybox:1.37 sh -c '
  test "$(cat /data/digger.db)" = database
  test "$(cat /movies/.digger.lock)" = lock
  test "$(stat -c %i /movies/.digger.lock)" = "$(cut -d " " -f 1 /data/lock-before)"
  test "$(stat -c "%u %g %a" /movies/unrelated.mkv)" = "$(cat /data/movie-before)"
  test -z "$(find /data /movies -name ".digger-write-check.*" -print)"
'
echo 'Digger storage checks passed: repaired permissions, non-root writes, Transmission access, preserved lock inode and unchanged media files.'
