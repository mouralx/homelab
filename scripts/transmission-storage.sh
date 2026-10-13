#!/bin/sh
set -eu
: "${TRANSMISSION_STORAGE_UID:?Transmission UID is required}"
: "${TRANSMISSION_STORAGE_GID:?Transmission GID is required}"
case "${1:-}" in
  prepare)
    if [ -L /config/settings.json ]; then echo 'Refusing a symlinked settings file.' >&2; exit 1; fi
    mkdir -p /config /downloads/complete /downloads/incomplete /watch
    if [ -f /config/settings.json ] && [ ! -e /config/settings.json.permissions-backup ]; then
      cp -p /config/settings.json /config/settings.json.permissions-backup
      chmod 600 /config/settings.json.permissions-backup
    fi
    # Repair configuration/state only; do not recursively change existing downloads.
    find /config -type d -exec chown "$TRANSMISSION_STORAGE_UID:$TRANSMISSION_STORAGE_GID" {} +
    find /config -type d -exec chmod u+rwx {} +
    find /config -type f -exec chown "$TRANSMISSION_STORAGE_UID:$TRANSMISSION_STORAGE_GID" {} +
    find /config -type f -exec chmod u+rw {} +
    for directory in /downloads /downloads/complete /downloads/incomplete /watch; do
      if [ -L "$directory" ]; then echo "Refusing a symlinked directory: $directory" >&2; exit 1; fi
      chown "$TRANSMISSION_STORAGE_UID:$TRANSMISSION_STORAGE_GID" "$directory"
      chmod u+rwx,g+rwx,g+s "$directory"
    done
    ;;
  check)
    probe=''
    trap 'if [ -n "$probe" ]; then rm -f "$probe"; fi' EXIT
    for directory in /config /downloads/complete /downloads/incomplete /watch; do
      probe=$(mktemp "$directory/.transmission-write-check.XXXXXX")
      rm -f "$probe"
      probe=''
    done
    for file in /config/settings.json /config/dht.dat /config/queue.json /config/bandwidth-groups.json; do
      if [ -e "$file" ] && { [ ! -r "$file" ] || [ ! -w "$file" ]; }; then
        echo "Transmission cannot read/write state file: $file" >&2; exit 1
      fi
    done
    echo 'Transmission can read/write its saved settings, state and download directories.'
    ;;
  *) echo 'Usage: transmission-storage.sh prepare|check' >&2; exit 1 ;;
esac
