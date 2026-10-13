#!/usr/bin/env bash
set -euo pipefail
# SIGHUP makes the running daemon read the effective settings after startup.
docker exec transmission sh -c 'pidof transmission-daemon | xargs -r kill -HUP'
echo 'Reloaded the running Transmission daemon settings.'
