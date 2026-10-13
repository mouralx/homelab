#!/bin/sh
set -eu

case "${1:-}" in
  prepare)
    : "${DIGGER_STORAGE_UID:?Digger UID is required}"
    : "${DIGGER_STORAGE_GID:?Shared media GID is required}"
    chown "$DIGGER_STORAGE_UID:$DIGGER_STORAGE_GID" /data
    chmod u+rwx /data
    chgrp "$DIGGER_STORAGE_GID" /movies
    chmod g+rwx,g+s /movies
    if [ -L /movies/.downloads ]; then
      echo 'Refusing to change a symlink: /movies/.downloads' >&2; exit 1
    fi
    mkdir -p /movies/.downloads
    chgrp "$DIGGER_STORAGE_GID" /movies/.downloads
    chmod g+rwx,g+s /movies/.downloads
    for directory in /movies/.downloads/tt[0-9]*; do
      id=${directory##*/tt}
      case "$id" in ''|*[!0-9]*) continue ;; esac
      if [ -L "$directory" ]; then echo "Refusing to change a symlink: $directory" >&2; exit 1; fi
      if [ -d "$directory" ]; then
        chgrp "$DIGGER_STORAGE_GID" "$directory"
        chmod g+rwx,g+s "$directory"
      fi
    done
    for file in /data/digger.db /data/digger.db.lock /data/digger.db-wal /data/digger.db-shm /movies/.digger.lock; do
      if [ -L "$file" ]; then
        echo "Refusing to change a symlink: $file" >&2; exit 1
      fi
      if [ -e "$file" ]; then
        if [ ! -f "$file" ]; then echo "Expected a regular file: $file" >&2; exit 1; fi
        if [ "$file" = /movies/.digger.lock ]; then
          chgrp "$DIGGER_STORAGE_GID" "$file"
          chmod g+rw "$file"
        else
          chown "$DIGGER_STORAGE_UID:$DIGGER_STORAGE_GID" "$file"
          chmod u+rw "$file"
        fi
      fi
    done
    ;;
  check)
    probe=''
    trap 'if [ -n "$probe" ]; then rm -f "$probe"; fi' EXIT
    for directory in /data /movies; do
      probe=$(mktemp "$directory/.digger-write-check.XXXXXX")
      rm -f "$probe"
      probe=''
    done
    for file in /data/digger.db /data/digger.db.lock /data/digger.db-wal /data/digger.db-shm /movies/.digger.lock; do
      if [ -e "$file" ] && [ ! -w "$file" ]; then
        echo "Digger cannot write existing file: $file" >&2; exit 1
      fi
    done
    echo 'Digger can write its database, media directory and existing lock files.'
    ;;
  *) echo 'Usage: digger-storage.sh prepare|check' >&2; exit 1 ;;
esac
